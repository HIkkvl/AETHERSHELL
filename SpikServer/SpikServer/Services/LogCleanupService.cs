using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AetherShell.Server.Data;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Фоновый сервис для автоматической очистки старых логов.
    /// Удаляет логи старше 90 дней каждые 24 часа.
    /// </summary>
    public class LogCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(24); // Проверяем раз в сутки
        private readonly int _retentionDays = 90; // Хранить логи 90 дней

        public LogCleanupService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine($"[LogCleanupService] Сервис очистки логов запущен. Хранение: {_retentionDays} дней");

            // Первая очистка при запуске
            try
            {
                await CleanupOldLogs();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LogCleanupService] Ошибка при первой очистке: {ex.Message}");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                    await CleanupOldLogs();
                }
                catch (OperationCanceledException)
                {
                    // Нормальное завершение при остановке сервиса
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LogCleanupService] Ошибка: {ex.Message}");
                }
            }
            
            Console.WriteLine("[LogCleanupService] Сервис очистки логов остановлен");
        }

        private async Task CleanupOldLogs()
        {
            using var scope = _scopeFactory.CreateScope();
            var clubs = scope.ServiceProvider.GetRequiredService<IClubRegistry>();
            var clubDbFactory = scope.ServiceProvider.GetRequiredService<IClubDbContextFactory>();

            var cutoffDate = DateTime.UtcNow.AddDays(-_retentionDays);

            foreach (var clubId in await clubs.ActiveClubIdsAsync())
            {
                try
                {
                    await using var db = clubDbFactory.Create(clubId);

                    var removedLogs = await db.AdminLogs
                        .Where(l => l.CreatedAt < cutoffDate)
                        .ExecuteDeleteAsync();

                    var removedMessages = await db.ChatMessages
                        .Where(m => m.CreatedAt < cutoffDate)
                        .ExecuteDeleteAsync();

                    if (removedLogs + removedMessages > 0)
                        Console.WriteLine($"[LogCleanupService] Клуб {clubId}: удалено {removedLogs} логов и {removedMessages} сообщений чата старше {_retentionDays} дней");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LogCleanupService] Клуб {clubId}: {ex.Message}");
                }
            }
        }
    }
}
