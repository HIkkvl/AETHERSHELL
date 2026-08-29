using Microsoft.AspNetCore.SignalR;
using AetherShell.Server.Constants;
using AetherShell.Server.Hubs;

namespace AetherShell.Server.Services
{

    public class SessionWorker : BackgroundService
    {
        private readonly SessionManager _sessionManager;
        private readonly IHubContext<ClubHub> _hubContext;

        public SessionWorker(SessionManager sessionManager, IHubContext<ClubHub> hubContext)
        {
            _sessionManager = sessionManager;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Просроченные сессии собираются сразу по всем клубам,
                    // а блокировка уходит в группу конкретного ПК конкретного клуба.
                    var expired = await _sessionManager.GetExpiredSessionsAsync();

                    foreach (var (clubId, pcName) in expired)
                    {
                        Console.WriteLine($"[Worker] Время вышло: клуб {clubId}, ПК {pcName}. Блокируем.");

                        await _sessionManager.StopSessionAsync(clubId, pcName);

                        await _hubContext.Clients
                            .Group(ClubHub.PcGroup(clubId, pcName))
                            .SendAsync(SignalRMethods.ReceiveLock, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Worker Error] {ex.Message}");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
