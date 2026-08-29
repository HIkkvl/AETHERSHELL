using AetherShell.Server.Data;
using AetherShell.Server.Models;
using System.Threading.Tasks;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Пишет действия персонала в AdminLog.
    ///
    /// Сервис намеренно scoped и использует контекст текущего запроса: раньше он
    /// поднимал собственный DI-скоуп, где клуб был неизвестен, и запись лога
    /// падала с исключением уже после того, как основное действие сохранилось.
    /// </summary>
    public class AuditLogger
    {
        private readonly ClubDbContext _db;

        public AuditLogger(ClubDbContext db)
        {
            _db = db;
        }

        public async Task LogAsync(string admin, string action, string target, string details)
        {
            _db.AdminLogs.Add(new AdminLog
            {
                AdminName = admin,
                ActionType = action,
                Target = target,
                Details = details,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
        }
    }
}
