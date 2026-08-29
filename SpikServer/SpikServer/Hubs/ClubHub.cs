using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Constants;
using AetherShell.Server.Data;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace AetherShell.Server.Hubs
{
    public class ClubHub : Hub
    {
        private const string ClubIdItem = "ClubId";
        private const string PcIdItem = "MacAddress";

        /// <summary>
        /// Подключённые ПК: ключ — "{clubId}:{pcId}", значение — ConnectionId.
        /// Ключ составной, потому что имена и MAC-адреса ПК уникальны только внутри клуба.
        /// </summary>
        private static readonly ConcurrentDictionary<string, string> _connectedPcs = new();

        private readonly SessionManager _sessionManager;
        private readonly IClubDbContextFactory _clubDb;
        private readonly IClubRegistry _clubs;
        private readonly IServiceScopeFactory _scopeFactory;

        public ClubHub(
            SessionManager sessionManager,
            IClubDbContextFactory clubDb,
            IClubRegistry clubs,
            IServiceScopeFactory scopeFactory)
        {
            _sessionManager = sessionManager;
            _clubDb = clubDb;
            _clubs = clubs;
            _scopeFactory = scopeFactory;
        }

        // ===== Ключи групп и словаря: единственное место, где задаётся формат =====

        public static string PcKey(int clubId, string pcId) => $"{clubId}:{pcId}";

        /// <summary>Группа конкретного ПК: адресные команды блокировки, выключения, чата.</summary>
        public static string PcGroup(int clubId, string pcId) => $"club:{clubId}:pc:{pcId}";

        /// <summary>Группа админов клуба: обновления дашборда и уведомления.</summary>
        public static string AdminGroup(int clubId) => $"club:{clubId}:admins";

        public static bool TryGetPcConnection(int clubId, string pcId, out string? connectionId)
            => _connectedPcs.TryGetValue(PcKey(clubId, pcId), out connectionId);

        public static bool IsPcOnline(int clubId, string pcId)
            => _connectedPcs.ContainsKey(PcKey(clubId, pcId));

        /// <summary>Имена ПК клуба, которые сейчас держат соединение.</summary>
        public static HashSet<string> OnlinePcIds(int clubId)
        {
            var prefix = $"{clubId}:";
            return _connectedPcs.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Select(k => k.Substring(prefix.Length))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        // ===== Жизненный цикл соединения =====

        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            if (httpContext == null) return;

            var clubId = await ResolveClubIdAsync(httpContext);
            if (clubId == null)
            {
                // Без клуба соединение бессмысленно: непонятно, чьи данные обслуживать.
                Context.Abort();
                return;
            }

            Context.Items[ClubIdItem] = clubId.Value;

            var pcId = httpContext.Request.Query["pc_id"].ToString();

            if (!string.IsNullOrEmpty(pcId))
            {
                _connectedPcs[PcKey(clubId.Value, pcId)] = Context.ConnectionId;
                Context.Items[PcIdItem] = pcId;

                Console.WriteLine($"[Hub] ПК подключился: клуб {clubId}, {pcId} ({Context.ConnectionId})");

                await Groups.AddToGroupAsync(Context.ConnectionId, PcGroup(clubId.Value, pcId));

                var isApproved = await SetComputerOnlineStatusAsync(clubId.Value, pcId, true);

                if (!isApproved)
                {
                    await Clients.Caller.SendAsync("PendingApproval");
                    Console.WriteLine($"[Hub] ПК {pcId} (клуб {clubId}) ожидает подтверждения");
                }
                else
                {
                    var session = await _sessionManager.GetActiveSessionAsync(clubId.Value, pcId);

                    if (session != null && session.EndTime > DateTime.UtcNow)
                    {
                        await Clients.Caller.SendAsync(SignalRMethods.ReceiveUnlock, session.EndTime);
                        Console.WriteLine($"[Hub] Восстановлена сессия для {pcId} (клуб {clubId})");
                    }
                    else
                    {
                        await Clients.Caller.SendAsync(SignalRMethods.ReceiveLock);
                    }
                }

                await Clients.Group(AdminGroup(clubId.Value)).SendAsync("DashboardUpdate");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (TryGetContext(out var clubId, out var pcId))
            {
                _connectedPcs.TryRemove(PcKey(clubId, pcId), out _);
                Console.WriteLine($"[Hub] Отключился: клуб {clubId}, {pcId}");

                await SetComputerOnlineStatusAsync(clubId, pcId, false);
                await Clients.Group(AdminGroup(clubId)).SendAsync("DashboardUpdate");
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ===== Методы, вызываемые клиентами =====

        /// <summary>Сообщение от ПК администраторам своего клуба.</summary>
        public async Task SendToAdmin(string message)
        {
            if (!TryGetContext(out var clubId, out var pcId))
            {
                Console.WriteLine("[Hub] SendToAdmin вызван без контекста ПК.");
                return;
            }

            await using var db = _clubDb.Create(clubId);

            var pc = await db.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            var realPcName = pc?.Name ?? pcId;

            db.ChatMessages.Add(new ChatMessage
            {
                PcName = realPcName,
                Message = message,
                IsFromAdmin = false,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            await Clients.Group(AdminGroup(clubId)).SendAsync("ReceiveMessageFromClient", realPcName, message);
        }

        /// <summary>Сообщение от администратора конкретному ПК своего клуба.</summary>
        public async Task SendToPc(string pcName, string message)
        {
            if (!IsStaff())
            {
                await Clients.Caller.SendAsync("Error", "Недостаточно прав");
                return;
            }

            if (!TryGetClubId(out var clubId))
            {
                await Clients.Caller.SendAsync("Error", "Клуб не определён");
                return;
            }

            await using (var db = _clubDb.Create(clubId))
            {
                db.ChatMessages.Add(new ChatMessage
                {
                    PcName = pcName,
                    Message = message,
                    IsFromAdmin = true,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            if (TryGetPcConnection(clubId, pcName, out var connId) && connId != null)
            {
                await Clients.Client(connId).SendAsync("ReceiveChatMessage", "Admin", message);
            }
        }

        public async Task JoinAdminGroup()
        {
            if (!IsStaff())
            {
                await Clients.Caller.SendAsync("Error", "Недостаточно прав для входа в группу админов");
                return;
            }

            if (!TryGetClubId(out var clubId))
            {
                await Clients.Caller.SendAsync("Error", "Клуб не определён");
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup(clubId));
        }

        public async Task<bool> IsPcApproved(string pcId)
        {
            if (!TryGetClubId(out var clubId)) return false;

            await using var db = _clubDb.Create(clubId);
            var pc = await db.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            return pc?.IsApproved ?? false;
        }

        /// <summary>
        /// Шелл сообщает, что сейчас в фокусе у клиента. Панель показывает это
        /// вместо безликого «в игре», поэтому вызывается при каждой смене окна.
        /// </summary>
        public async Task UpdateCurrentApp(string? processName, string? windowTitle)
        {
            if (!TryGetContext(out var clubId, out var pcId)) return;

            await using var db = _clubDb.Create(clubId);
            var pc = await db.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            if (pc == null) return;

            var normalized = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();

            // Время начала не сбрасываем, пока приложение то же: панель показывает,
            // сколько человек уже играет.
            if (!string.Equals(pc.CurrentApp, normalized, StringComparison.OrdinalIgnoreCase))
                pc.CurrentAppSince = normalized == null ? null : DateTime.UtcNow;

            pc.CurrentApp = normalized;
            pc.CurrentAppTitle = string.IsNullOrWhiteSpace(windowTitle) ? null : windowTitle.Trim();

            await db.SaveChangesAsync();
            await Clients.Group(AdminGroup(clubId)).SendAsync("DashboardUpdate");
        }

        public async Task UpdateSystemInfo(string ipAddress, string cpuName, int ramTotalMb, int ramUsedMb,
                                           string gpuName, string diskInfo, string osVersion,
                                           string? macAddress = null)
        {
            if (!TryGetContext(out var clubId, out var pcId)) return;

            await using var db = _clubDb.Create(clubId);
            var pc = await db.Computers.FirstOrDefaultAsync(c => c.Name == pcId);

            if (pc == null) return;

            // MAC больше не личность ПК, но админу он нужен для поиска машины в сети.
            if (!string.IsNullOrWhiteSpace(macAddress)) pc.MacAddress = macAddress;

            pc.IpAddress = ipAddress;
            pc.CpuName = cpuName;
            pc.RamTotalMb = ramTotalMb;
            pc.RamUsedMb = ramUsedMb;
            pc.GpuName = gpuName;
            pc.DiskInfo = diskInfo;
            pc.OsVersion = osVersion;
            pc.SystemInfoUpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await Clients.Group(AdminGroup(clubId)).SendAsync("DashboardUpdate");
        }

        /// <summary>
        /// Отмечает ПК онлайн/офлайн, при первом подключении регистрирует его в клубе.
        /// Возвращает признак подтверждения, чтобы вызывающий решил, показывать ли экран ожидания.
        /// </summary>
        private async Task<bool> SetComputerOnlineStatusAsync(int clubId, string pcId, bool isOnline)
        {
            using var scope = _scopeFactory.CreateScope();
            var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            await using var db = _clubDb.Create(clubId);

            var pc = await db.Computers.FirstOrDefaultAsync(c => c.Name == pcId);

            if (pc == null)
            {
                if (!isOnline) return false;

                var requireApproval = await platform.Clubs
                    .Where(c => c.Id == clubId)
                    .Select(c => c.RequireComputerApproval)
                    .FirstOrDefaultAsync();

                var newPc = new Computer
                {
                    Name = pcId,
                    HardwareId = pcId,
                    DisplayName = "Новый ПК",
                    GroupName = "Новые",
                    IsOnline = true,
                    IsApproved = !requireApproval,
                    Status = ComputerStatus.Locked,
                    LastSeenAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                db.Computers.Add(newPc);
                await db.SaveChangesAsync();

                Console.WriteLine($"[Hub] Зарегистрирован новый ПК в клубе {clubId}: {pcId} (подтверждён: {newPc.IsApproved})");

                if (!newPc.IsApproved)
                    await Clients.Group(AdminGroup(clubId)).SendAsync("NewPcPendingApproval", pcId);

                return newPc.IsApproved;
            }

            pc.IsOnline = isOnline;
            pc.LastSeenAt = DateTime.UtcNow;

            if (isOnline)
            {
                var hasActiveSession = pc.SessionEndTime.HasValue && pc.SessionEndTime.Value > DateTime.UtcNow;
                pc.Status = hasActiveSession ? ComputerStatus.Active : ComputerStatus.Locked;
            }
            else
            {
                pc.Status = ComputerStatus.Offline;
                pc.CurrentApp = null;
                pc.CurrentAppTitle = null;
                pc.CurrentAppSince = null;
            }

            await db.SaveChangesAsync();

            // Активность любого ПК считается признаком жизни клуба — заменяет прежний heartbeat.
            // Запись о клубе живёт в платформенной базе, поэтому это отдельное сохранение.
            var club = await platform.Clubs.FirstOrDefaultAsync(c => c.Id == clubId);
            if (club != null)
            {
                club.LastSeenAt = DateTime.UtcNow;
                await platform.SaveChangesAsync();
            }

            return pc.IsApproved;
        }

        // ===== Вспомогательное =====

        private bool IsStaff()
        {
            var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            return role is "Admin" or "Senior" or "Super"
                or AccountRoles.Owner or AccountRoles.PlatformAdmin;
        }

        private bool TryGetClubId(out int clubId)
        {
            clubId = 0;
            if (Context.Items.TryGetValue(ClubIdItem, out var raw) && raw is int value)
            {
                clubId = value;
                return clubId > 0;
            }
            return false;
        }

        private bool TryGetContext(out int clubId, out string pcId)
        {
            pcId = "";
            if (!TryGetClubId(out clubId)) return false;
            if (Context.Items.TryGetValue(PcIdItem, out var raw) && raw is string value && !string.IsNullOrEmpty(value))
            {
                pcId = value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Клуб соединения: у шелла это ключ из server.config, у веб-панели —
        /// claim клубного токена либо clubId с проверкой владения.
        /// </summary>
        private async Task<int?> ResolveClubIdAsync(HttpContext http)
        {
            var claimClubId = Context.User?.FindFirst(AetherClaims.ClubId)?.Value;
            if (int.TryParse(claimClubId, out var fromClaim) && fromClaim > 0)
                return fromClaim;

            var clubKey = FirstNonEmpty(
                http.Request.Query["clubKey"].ToString(),
                http.Request.Headers["X-Club-Key"].ToString());

            if (!string.IsNullOrWhiteSpace(clubKey))
                return await _clubs.ResolveByEnrollmentKeyAsync(clubKey);

            var rawClubId = FirstNonEmpty(
                http.Request.Query["clubId"].ToString(),
                http.Request.Headers["X-Club-Id"].ToString());

            if (!int.TryParse(rawClubId, out var requested) || requested <= 0)
                return null;

            var role = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (role == AccountRoles.PlatformAdmin)
                return requested;

            if (int.TryParse(Context.User?.FindFirst(AetherClaims.AccountId)?.Value, out var accountId))
            {
                using var scope = _scopeFactory.CreateScope();
                var platform = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

                var owns = await platform.Clubs
                    .AsNoTracking()
                    .AnyAsync(c => c.Id == requested && c.OwnerId == accountId && c.IsActive);
                if (owns) return requested;
            }

            return null;
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }
}
