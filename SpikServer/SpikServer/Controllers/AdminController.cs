using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Constants;
using AetherShell.Server.Data;
using AetherShell.Server.DTOs;
using AetherShell.Server.Filters;
using AetherShell.Server.Hubs;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherShell.Server.Controllers
{

    [Route("api/[controller]")]
    [ApiController]
    [RequireClub]
    public class AdminController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly PlatformDbContext _platform;
        private readonly IHubContext<ClubHub> _hubContext;
        private readonly SessionManager _sessionManager;
        private readonly AuditLogger _logger;
        private readonly ICurrentClub _currentClub;
        private readonly ClubRealtimeNotifier _live;

        public AdminController(
            ClubDbContext context,
            PlatformDbContext platform,
            IHubContext<ClubHub> hubContext,
            SessionManager sessionManager,
            AuditLogger logger,
            ICurrentClub currentClub,
            ClubRealtimeNotifier live)
        {
            _context = context;
            _platform = platform;
            _hubContext = hubContext;
            _sessionManager = sessionManager;
            _logger = logger;
            _currentClub = currentClub;
            _live = live;
        }

        /// <summary>Клуб текущего запроса. Гарантирован атрибутом <see cref="RequireClubAttribute"/>.</summary>
        private int ClubId => _currentClub.ClubId!.Value;

        /// <summary>Посетители сети: баланс у них общий на все филиалы.</summary>
        private IQueryable<Client> NetworkClients => _platform.Clients.Where(c => c.NetworkId == _currentClub.NetworkId);

        // ==========================================
        // 1. ПОЛУЧЕНИЕ СПИСКА
        // ==========================================
        [HttpGet("computers")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> GetComputers()
        {
            var dbComputers = await _context.Computers.OrderBy(c => c.Name).ToListAsync();
            var result = new List<ComputerTableDto>();

            foreach (var pc in dbComputers)
            {
                result.Add(new ComputerTableDto
                {
                    Id = pc.Id,
                    PcName = pc.Name,
                    NameToDisplay = !string.IsNullOrEmpty(pc.DisplayName) ? pc.DisplayName : pc.Name,
                    GroupName = pc.GroupName ?? "Общий зал",
                    IsOnline = pc.IsOnline,
                    CurrentUser = pc.CurrentUser,
                    SessionEndTime = pc.SessionEndTime,
                    Status = pc.Status.ToString(),
                    IsApproved = pc.IsApproved,
                    LastSeenAt = pc.LastSeenAt,
                    CurrentApp = pc.CurrentApp,
                    CurrentAppTitle = pc.CurrentAppTitle,
                    CurrentAppSince = pc.CurrentAppSince,
                    MapX = pc.MapX,
                    MapY = pc.MapY
                });
            }
            return Ok(result);
        }

        /// <summary>
        /// Сохраняет расстановку ПК на карте клуба (проценты холста 0…100).
        /// </summary>
        [HttpPut("computers/layout")]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> SaveComputerLayout([FromBody] ComputerLayoutRequest request)
        {
            if (request?.Items == null || request.Items.Count == 0)
                return BadRequest(new { error = "Пустой список позиций" });

            var ids = request.Items.Select(i => i.Id).Distinct().ToList();
            var computers = await _context.Computers
                .Where(c => ids.Contains(c.Id) && c.IsApproved)
                .ToListAsync();

            foreach (var item in request.Items)
            {
                var pc = computers.FirstOrDefault(c => c.Id == item.Id);
                if (pc == null) continue;

                pc.MapX = Math.Clamp(item.MapX, 0, 100);
                pc.MapY = Math.Clamp(item.MapY, 0, 100);
            }

            await _context.SaveChangesAsync();
            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Map", "Layout",
                $"Обновлена карта клуба, ПК: {computers.Count}");
            await _live.ComputersUpdatedAsync();
            return Ok(new { message = "Расстановка сохранена", count = computers.Count });
        }

        // ==========================================
        // 1.0.1 ПОЛУЧЕНИЕ ДЕТАЛЬНОЙ ИНФОРМАЦИИ О ПК
        // ==========================================
        [HttpGet("computer-details")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> GetComputerDetails([FromQuery] string pcId)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            if (pc == null) return NotFound("Компьютер не найден");

            var details = new ComputerDetailsDto
            {
                Id = pc.Id,
                PcName = pc.Name,
                DisplayName = !string.IsNullOrEmpty(pc.DisplayName) ? pc.DisplayName : pc.Name,
                GroupName = pc.GroupName ?? "Общий зал",
                IsOnline = pc.IsOnline,
                Status = pc.Status.ToString(),
                CurrentUser = pc.CurrentUser,
                SessionEndTime = pc.SessionEndTime,
                LastSeenAt = pc.LastSeenAt,
                CreatedAt = pc.CreatedAt,

                CurrentApp = pc.CurrentApp,
                CurrentAppTitle = pc.CurrentAppTitle,
                CurrentAppSince = pc.CurrentAppSince,

                // Системная информация
                IpAddress = pc.IpAddress,
                // Name — это HardwareId, MAC теперь отдельное справочное поле.
                MacAddress = pc.MacAddress,
                CpuName = pc.CpuName,
                RamTotalMb = pc.RamTotalMb,
                RamUsedMb = pc.RamUsedMb,
                GpuName = pc.GpuName,
                DiskInfo = pc.DiskInfo,
                OsVersion = pc.OsVersion,
                SystemInfoUpdatedAt = pc.SystemInfoUpdatedAt
            };

            return Ok(details);
        }

        // ==========================================
        // 1.1 ПОЛУЧЕНИЕ ПК ОЖИДАЮЩИХ ПОДТВЕРЖДЕНИЯ
        // ==========================================
        [HttpGet("pending-computers")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> GetPendingComputers()
        {
            var pending = await _context.Computers
                .Where(c => !c.IsApproved)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new {
                    c.Id,
                    c.Name,
                    c.DisplayName,
                    c.IsOnline,
                    c.CreatedAt
                })
                .ToListAsync();

            return Ok(pending);
        }

        // ==========================================
        // 1.2 ПОДТВЕРЖДЕНИЕ ПК
        // ==========================================
        [HttpPost("approve-computer")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> ApproveComputer([FromQuery] string pcId, [FromQuery] string? displayName = null)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            if (pc == null) return NotFound("ПК не найден");

            pc.IsApproved = true;
            pc.GroupName = "Общий зал";
            
            if (!string.IsNullOrEmpty(displayName))
                pc.DisplayName = displayName;

            await _context.SaveChangesAsync();

            // Уведомляем клиент что он подтверждён
            if (ClubHub.TryGetPcConnection(ClubId, pcId, out var connectionId) && connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("Approved");
                // Отправляем Lock чтобы показать экран входа
                await _hubContext.Clients.Client(connectionId).SendAsync(SignalRMethods.ReceiveLock);
            }

            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Settings", pc.DisplayName, 
                $"ПК подтверждён администратором");
            await _live.ComputersUpdatedAsync();

            return Ok(new { message = "ПК подтверждён" });
        }

        // ==========================================
        // 1.3 ОТКЛОНЕНИЕ/УДАЛЕНИЕ ПК
        // ==========================================
        [HttpDelete("computer")]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> DeleteComputer([FromQuery] string pcId)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            if (pc == null) return NotFound("ПК не найден");

            string pcName = pc.DisplayName;
            _context.Computers.Remove(pc);
            await _context.SaveChangesAsync();

            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Settings", pcName, "ПК удалён");
            await _live.ComputersUpdatedAsync();

            return Ok(new { message = "ПК удалён" });
        }

        // =========================
        // 2. ДАШБОРД
        // =========================
        [HttpGet("dashboard")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> GetDashboard()
        {
            // 1. Статистика по объектам
            var total = await _context.Computers.Where(c => c.IsApproved).CountAsync();
            var online = await _context.Computers.CountAsync(c => c.IsOnline && c.IsApproved);
            var active = await _context.Computers.CountAsync(c => c.Status == ComputerStatus.Active && c.IsApproved);
            var errorCount = await _context.Computers.CountAsync(c => c.Status == ComputerStatus.Error && c.IsApproved);
            var pendingCount = await _context.Computers.CountAsync(c => !c.IsApproved);
            var users = await NetworkClients.CountAsync();
            var apps = await _context.AppItems.CountAsync();

            var todayStart = DateTime.UtcNow.Date;

            decimal revenue = await _context.Orders
                .Where(o => o.CreatedAt >= todayStart && o.Status != OrderStatus.Cancelled)
                .SumAsync(o => o.TotalPrice);

            decimal revenueKaspi = Math.Round(revenue * 0.7m, 2);
            decimal revenuePackages = revenue - revenueKaspi;

            string topAppName = "CS 2";
            int topAppCount = 12; 

            var stats = new DashboardStats
            {
                TotalComputers = total,
                EnabledComputers = online,
                DisabledComputers = total - online,
                ActiveComputers = active,
                ErrorComputers = errorCount,
                PendingComputers = pendingCount,
                UsersCount = users,
                AppsCount = apps,

                TopAppCount = topAppCount,
                TopAppName = topAppName,

                RevenueTotal = revenue,
                RevenueKaspi = revenueKaspi,
                RevenuePackages = revenuePackages
            };

            // ПК со статусом Error или Offline
            var errorPcs = await _context.Computers
                .Where(c => c.Status == ComputerStatus.Error || (!c.IsOnline && c.IsApproved))
                .OrderByDescending(c => c.Status == ComputerStatus.Error)
                .ThenBy(c => c.Name)
                .Select(c => new {
                    nameToDisplay = !string.IsNullOrEmpty(c.DisplayName) ? c.DisplayName : c.Name,
                    pcName = c.Name,
                    status = c.Status.ToString(),
                    lastSeen = c.LastSeenAt.HasValue 
                        ? c.LastSeenAt.Value.ToString("dd.MM HH:mm") 
                        : "Неизвестно"
                })
                .ToListAsync();

            // ПК ожидающие подтверждения
            var pendingPcs = await _context.Computers
                .Where(c => !c.IsApproved)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new {
                    pcName = c.Name,
                    isOnline = c.IsOnline,
                    createdAt = c.CreatedAt.ToString("dd.MM HH:mm")
                })
                .ToListAsync();

            return Ok(new { stats, error_pcs = errorPcs, pending_pcs = pendingPcs });
        }

        // ==========================================
        // 3. ЗАПУСК СЕССИИ (АДМИНОМ)
        // ==========================================
        // ЗАПУСТИТЬ ИЛИ ПРОДЛИТЬ СЕССИЮ
        [HttpPost("start")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> StartSession(string pcId, int minutes)
        {
            var computer = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            if (computer == null) return NotFound("ПК не найден");

            // --- ЛОГИКА ВРЕМЕНИ (ИСПРАВЛЕННАЯ) ---
            var now = DateTime.UtcNow; // Работаем только с UTC

            // 1. Проверяем, есть ли у человека время, которое ЕЩЕ НЕ ИСТЕКЛО
            bool hasActiveTime = computer.SessionEndTime.HasValue && computer.SessionEndTime.Value > now;

            if (hasActiveTime)
            {
                // СЦЕНАРИЙ: ПРОДЛЕНИЕ
                computer.SessionEndTime = computer.SessionEndTime.Value.AddMinutes(minutes);
                computer.SessionSavesRemaining = true; // админское время — несгораемое
            }
            else
            {
                // СЦЕНАРИЙ: НОВАЯ СЕССИЯ
                computer.SessionEndTime = now.AddMinutes(minutes);
                computer.SessionSavesRemaining = true;
            }
            // -------------------------------------

            // Всегда ставим онлайн при добавлении времени
            computer.IsOnline = true;

            await _context.SaveChangesAsync();

            // Отправляем команду клиенту (Shell) на обновление таймера
            if (ClubHub.TryGetPcConnection(ClubId, computer.Name, out var connectionId) && connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync(SignalRMethods.ReceiveUnlock, computer.SessionEndTime);
            }

            var adminName = User.Identity?.Name ?? "Admin";
            string action = hasActiveTime ? "Продление" : "Новая сессия";

            await _logger.LogAsync(adminName, "Session", computer.Name,
                $"{action}: +{minutes} мин. Конец: {computer.SessionEndTime?.ToLocalTime()}");
            await _live.ComputersUpdatedAsync();

            return Ok(new { message = "Время обновлено" });
        }

        // ==========================================
        // 4. ОСТАНОВКА СЕССИИ (СОХРАНЕНИЕ ВРЕМЕНИ)
        // ==========================================
        [HttpPost("stop")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> StopSession(string pcId)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            string pcNameForLog = pc != null ? pc.DisplayName : pcId;

            if (pc != null)
            {
                // --- ЛОГИКА СОХРАНЕНИЯ ВРЕМЕНИ ---

                if (!string.IsNullOrEmpty(pc.CurrentUser) && pc.SessionEndTime.HasValue)
                {
                    var client = await NetworkClients.FirstOrDefaultAsync(c => c.Username == pc.CurrentUser);

                    if (client != null && pc.SessionSavesRemaining)
                    {
                        var now = DateTime.UtcNow;

                        if (pc.SessionEndTime.Value > now)
                        {
                            var diff = pc.SessionEndTime.Value - now;
                            int remainingMinutes = (int)diff.TotalMinutes;

                            if (remainingMinutes > 0)
                            {
                                client.RemainingMinutes += remainingMinutes;
                                await _platform.SaveChangesAsync();

                                await _logger.LogAsync("System", "SaveTime", pcNameForLog,
                                    $"Сохранено {remainingMinutes} мин. на аккаунт {client.Username}");
                            }
                        }
                    }
                    else if (client != null && !pc.SessionSavesRemaining)
                    {
                        await _logger.LogAsync("System", "SaveTime", pcNameForLog,
                            $"Сгораемый пакет: остаток не сохранён ({client.Username})");
                    }
                }
                // --- КОНЕЦ ЛОГИКИ ---

                pc.CurrentUser = null;
                pc.SessionEndTime = null;
                pc.SessionSavesRemaining = true;
                await _context.SaveChangesAsync();
            }

            if (ClubHub.TryGetPcConnection(ClubId, pcId, out var connectionId) && connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync(SignalRMethods.ReceiveLock);
            }

            await _sessionManager.StopSessionAsync(ClubId, pcId);
            await _logger.LogAsync("Admin", "Session", pcNameForLog, "Завершение сессии (время сохранено)");
            await _live.ComputersUpdatedAsync();

            return Ok(new { message = "Stopped" });
        }

        // ==========================================
        // 5. ПЕРЕИМЕНОВАНИЕ
        // ==========================================
        [HttpPost("rename")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> RenamePc(string pcId, string newName)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            if (pc == null) return NotFound("Компьютер не найден");

            string oldName = pc.DisplayName;
            pc.DisplayName = newName;

            if (newName.ToUpper().Contains("VIP"))
                pc.GroupName = "VIP Комната";
            else
                pc.GroupName = "Общий зал";

            await _context.SaveChangesAsync();
            await _logger.LogAsync("Admin", "Settings", pcId, $"Переименование: {oldName} -> {newName}");
            await _live.ComputersUpdatedAsync();

            return Ok(new { message = "Переименовано" });
        }

        // ==========================================
        // 6. УПРАВЛЕНИЕ ПИТАНИЕМ
        // ==========================================

        [HttpPost("shutdown")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> ShutdownPc(string pcId)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            string pcNameForLog = pc != null ? pc.DisplayName : pcId;

            if (ClubHub.TryGetPcConnection(ClubId, pcId, out var connectionId) && connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveShutdown");
                await _logger.LogAsync("Admin", "Power", pcNameForLog, "Выключение компьютера");
                return Ok(new { message = "Команда отправлена" });
            }
            return NotFound("Компьютер не в сети");
        }

        [HttpPost("reboot")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> RebootPc(string pcId)
        {
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);
            string pcNameForLog = pc != null ? pc.DisplayName : pcId;

            if (ClubHub.TryGetPcConnection(ClubId, pcId, out var connectionId) && connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveReboot");
                await _logger.LogAsync("Admin", "Power", pcNameForLog, "Перезагрузка компьютера");
                return Ok(new { message = "Команда отправлена" });
            }
            return NotFound("Компьютер не в сети");
        }

        // ==========================================
        // 7. ИСТОРИЯ
        // ==========================================
        [Authorize(Roles = "Senior,Super")]
        [HttpGet("logs")]
        public async Task<IActionResult> GetLogs([FromQuery] string? type = null)
        {
            var query = _context.AdminLogs.AsQueryable();

            if (!string.IsNullOrEmpty(type) && type != "All")
            {
                query = query.Where(l => l.ActionType == type);
            }
            // -----------------------------

            var logs = await query
                .OrderByDescending(l => l.CreatedAt)
                .Take(100)
                .ToListAsync();

            return Ok(logs);
        }

        // ==========================================
        // 8. МАССОВАЯ РАССЫЛКА СООБЩЕНИЙ
        // ==========================================
        [HttpPost("broadcast")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> BroadcastMessage([FromBody] BroadcastRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Message))
                return BadRequest("Сообщение не может быть пустым");

            int sentCount = 0;

            foreach (var pcName in ClubHub.OnlinePcIds(ClubId))
            {
                if (ClubHub.TryGetPcConnection(ClubId, pcName, out var connId) && connId != null)
                {
                    await _hubContext.Clients.Client(connId).SendAsync("ReceiveChatMessage", "Admin", request.Message);
                    
                    // Сохраняем в историю чата
                    _context.ChatMessages.Add(new ChatMessage
                    {
                        PcName = pcName,
                        Message = request.Message,
                        IsFromAdmin = true,
                        CreatedAt = DateTime.UtcNow
                    });
                    sentCount++;
                }
            }

            await _context.SaveChangesAsync();
            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Broadcast", "All", 
                $"Массовая рассылка: \"{request.Message}\" ({sentCount} ПК)");

            return Ok(new { message = $"Отправлено на {sentCount} ПК", count = sentCount });
        }

        // ==========================================
        // 9. ВЫКЛЮЧИТЬ ВЕСЬ ЗАЛ
        // ==========================================
        [HttpPost("shutdown-all")]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> ShutdownAll()
        {
            int count = 0;

            foreach (var pcName in ClubHub.OnlinePcIds(ClubId))
            {
                if (ClubHub.TryGetPcConnection(ClubId, pcName, out var connId) && connId != null)
                {
                    await _hubContext.Clients.Client(connId).SendAsync("ReceiveShutdown");
                    count++;
                }
            }

            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Power", "All", 
                $"Выключение всего зала ({count} ПК)");

            return Ok(new { message = $"Команда выключения отправлена на {count} ПК", count });
        }

        // ==========================================
        // 10. ПЕРЕЗАГРУЗИТЬ ВЕСЬ ЗАЛ
        // ==========================================
        [HttpPost("reboot-all")]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> RebootAll()
        {
            int count = 0;

            foreach (var pcName in ClubHub.OnlinePcIds(ClubId))
            {
                if (ClubHub.TryGetPcConnection(ClubId, pcName, out var connId) && connId != null)
                {
                    await _hubContext.Clients.Client(connId).SendAsync("ReceiveReboot");
                    count++;
                }
            }

            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Power", "All", 
                $"Перезагрузка всего зала ({count} ПК)");

            return Ok(new { message = $"Команда перезагрузки отправлена на {count} ПК", count });
        }

        // ==========================================
        // 11. СКАЧАТЬ ОТЧЁТ
        // ==========================================
        [HttpGet("report")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> GetReport([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            // PostgreSQL требует UTC DateTime
            var fromDate = from.HasValue 
                ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) 
                : DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
            var toDate = to.HasValue 
                ? DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc) 
                : DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);

            // Статистика по заказам
            var orders = await _context.Orders
                .Where(o => o.CreatedAt >= fromDate && o.CreatedAt < toDate)
                .ToListAsync();

            var totalRevenue = orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalPrice);
            var ordersCount = orders.Count;
            var cancelledCount = orders.Count(o => o.Status == OrderStatus.Cancelled);

            // Статистика по сессиям (логи)
            var sessionLogs = await _context.AdminLogs
                .Where(l => l.CreatedAt >= fromDate && l.CreatedAt < toDate && l.ActionType == "Session")
                .CountAsync();

            // Новые пользователи
            var newUsers = await NetworkClients
                .Where(c => c.CreatedAt >= fromDate && c.CreatedAt < toDate)
                .CountAsync();

            // Топ ПК по использованию (по логам)
            var topPcs = await _context.AdminLogs
                .Where(l => l.CreatedAt >= fromDate && l.CreatedAt < toDate && l.ActionType == "Session")
                .GroupBy(l => l.Target)
                .Select(g => new { PcName = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            // Формируем CSV
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("ОТЧЁТ КОМПЬЮТЕРНОГО КЛУБА SPIK");
            csv.AppendLine($"Период: {fromDate:dd.MM.yyyy} - {toDate:dd.MM.yyyy}");
            csv.AppendLine();
            csv.AppendLine("=== ФИНАНСЫ ===");
            csv.AppendLine($"Общая выручка;{totalRevenue} тг");
            csv.AppendLine($"Заказов всего;{ordersCount}");
            csv.AppendLine($"Отменено;{cancelledCount}");
            csv.AppendLine();
            csv.AppendLine("=== АКТИВНОСТЬ ===");
            csv.AppendLine($"Сессий запущено;{sessionLogs}");
            csv.AppendLine($"Новых пользователей;{newUsers}");
            csv.AppendLine();
            csv.AppendLine("=== ТОП ПК ===");
            csv.AppendLine("ПК;Сессий");
            foreach (var pc in topPcs)
            {
                csv.AppendLine($"{pc.PcName};{pc.Count}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"report_{fromDate:yyyyMMdd}_{toDate:yyyyMMdd}.csv");
        }
    }

    public class BroadcastRequest
    {
        public string Message { get; set; } = "";
    }
}