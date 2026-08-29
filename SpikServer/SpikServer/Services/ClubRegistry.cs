using AetherShell.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Список клубов платформы для кода, который работает вне HTTP-запроса.
    /// Фоновым сервисам нужно знать, по каким базам обходить круг, а хабу —
    /// сопоставить ключ из server.config с клубом.
    /// </summary>
    public interface IClubRegistry
    {
        Task<List<int>> ActiveClubIdsAsync();

        /// <summary>Клуб по ключу подключения из server.config, либо null.</summary>
        Task<int?> ResolveByEnrollmentKeyAsync(string enrollmentKey);
    }

    public class ClubRegistry : IClubRegistry
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ClubRegistry(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<List<int>> ActiveClubIdsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

            return await db.Clubs
                .AsNoTracking()
                .Where(c => c.IsActive)
                .Select(c => c.Id)
                .ToListAsync();
        }

        public async Task<int?> ResolveByEnrollmentKeyAsync(string enrollmentKey)
        {
            var key = enrollmentKey.Trim();

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

            return await db.Clubs
                .AsNoTracking()
                .Where(c => c.EnrollmentKey == key && c.IsActive)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();
        }
    }
}
