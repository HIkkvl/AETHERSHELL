using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AetherShell.Server.Data;
using AetherShell.Server.Hubs;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Фоновый сервис для мониторинга статуса компьютеров.
    /// Устанавливает статус ERROR если ПК был Online, но не отвечает дольше заданного времени.
    /// </summary>
    public class ComputerStatusService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<ClubHub> _hubContext;
        private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(30);
        private readonly TimeSpan _errorThreshold = TimeSpan.FromMinutes(2); // Через 2 мин без ответа = ERROR

        public ComputerStatusService(IServiceScopeFactory scopeFactory, IHubContext<ClubHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
        }

        // Реестр и фабрика контекстов берутся из скоупа: PlatformDbContext scoped,
        // а фоновый сервис живёт всё время работы приложения.

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("[ComputerStatusService] Сервис мониторинга статусов запущен");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckComputerStatuses();
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Нормальное завершение при остановке сервиса
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ComputerStatusService] Ошибка: {ex.Message}");
                }
            }
            
            Console.WriteLine("[ComputerStatusService] Сервис мониторинга остановлен");
        }

        private async Task CheckComputerStatuses()
        {
            using var scope = _scopeFactory.CreateScope();
            var clubs = scope.ServiceProvider.GetRequiredService<IClubRegistry>();
            var clubDbFactory = scope.ServiceProvider.GetRequiredService<IClubDbContextFactory>();

            foreach (var clubId in await clubs.ActiveClubIdsAsync())
            {
                try
                {
                    await CheckClubAsync(clubId, clubDbFactory);
                }
                catch (Exception ex)
                {
                    // Один проблемный клуб не должен останавливать обход остальных.
                    Console.WriteLine($"[ComputerStatusService] Клуб {clubId}: {ex.Message}");
                }
            }
        }

        private async Task CheckClubAsync(int clubId, IClubDbContextFactory clubDbFactory)
        {
            await using var db = clubDbFactory.Create(clubId);

            var now = DateTime.UtcNow;
            var threshold = now - _errorThreshold;

            var watched = await db.Computers
                .Where(c => (c.IsOnline && c.IsApproved) || c.Status == ComputerStatus.Error)
                .ToListAsync();

            if (watched.Count == 0) return;

            var connected = ClubHub.OnlinePcIds(clubId);
            var changed = false;

            foreach (var pc in watched)
            {
                var isReallyConnected = connected.Contains(pc.Name);

                if (pc.Status == ComputerStatus.Error)
                {
                    if (!isReallyConnected) continue;

                    pc.IsOnline = true;
                    pc.Status = ComputerStatus.Locked;
                    pc.LastSeenAt = now;
                    changed = true;
                    Console.WriteLine($"[ComputerStatusService] ПК {pc.DisplayName} ({pc.Name}, клуб {clubId}) восстановлен из ERROR");
                    continue;
                }

                if (!isReallyConnected)
                {
                    if (pc.LastSeenAt.HasValue && pc.LastSeenAt.Value < threshold)
                    {
                        pc.Status = ComputerStatus.Error;
                        pc.IsOnline = false;
                        // Игра на пропавшем ПК больше не актуальна.
                        pc.CurrentApp = null;
                        pc.CurrentAppTitle = null;
                        pc.CurrentAppSince = null;
                        changed = true;
                        Console.WriteLine($"[ComputerStatusService] ПК {pc.DisplayName} ({pc.Name}, клуб {clubId}) -> ERROR (не отвечает)");
                    }
                    continue;
                }

                pc.LastSeenAt = now;

                var hasActiveSession = pc.SessionEndTime.HasValue && pc.SessionEndTime.Value > now;
                var correctStatus = hasActiveSession ? ComputerStatus.Active : ComputerStatus.Locked;

                if (pc.Status != correctStatus)
                {
                    pc.Status = correctStatus;
                    changed = true;
                }
            }

            if (!changed) return;

            await db.SaveChangesAsync();
            await _hubContext.Clients.Group(ClubHub.AdminGroup(clubId)).SendAsync("DashboardUpdate");
        }
    }
}
