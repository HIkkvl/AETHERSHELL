using AetherShell.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Services
{
    /// <summary>Ссылка на ПК в конкретном клубе.</summary>
    public readonly record struct ClubPc(int ClubId, string PcName);

    public class SessionManager
    {
        private readonly IClubDbContextFactory _clubDb;
        private readonly IClubRegistry _clubs;

        public SessionManager(IClubDbContextFactory clubDb, IClubRegistry clubs)
        {
            _clubDb = clubDb;
            _clubs = clubs;
        }

        public async Task StartSessionAsync(int clubId, string pcName, DateTime endTime, string? username = null)
        {
            await using var db = _clubDb.Create(clubId);

            var oldSession = await db.Sessions
                .FirstOrDefaultAsync(s => s.ComputerName == pcName && s.IsActive);

            if (oldSession != null)
            {
                oldSession.IsActive = false;
                oldSession.EndTime = DateTime.UtcNow;
            }

            db.Sessions.Add(new Session
            {
                ComputerName = pcName,
                Username = username,
                StartTime = DateTime.UtcNow,
                EndTime = endTime.ToUniversalTime(),
                IsActive = true
            });

            await db.SaveChangesAsync();
        }

        public async Task StopSessionAsync(int clubId, string pcName)
        {
            await using var db = _clubDb.Create(clubId);

            var session = await db.Sessions
                .FirstOrDefaultAsync(s => s.ComputerName == pcName && s.IsActive);

            if (session != null)
            {
                session.IsActive = false;
                await db.SaveChangesAsync();
            }
        }

        public async Task<DateTime?> GetEndTimeAsync(int clubId, string pcName)
        {
            var session = await GetActiveSessionAsync(clubId, pcName);
            return session?.EndTime;
        }

        /// <summary>
        /// Просроченные сессии по всем клубам. Один общий запрос больше невозможен —
        /// базы физически разные, поэтому обходим клубы по реестру.
        /// </summary>
        public async Task<List<ClubPc>> GetExpiredSessionsAsync()
        {
            var result = new List<ClubPc>();
            var now = DateTime.UtcNow;

            foreach (var clubId in await _clubs.ActiveClubIdsAsync())
            {
                try
                {
                    await using var db = _clubDb.Create(clubId);

                    var expired = await db.Sessions
                        .Where(s => s.IsActive && s.EndTime <= now)
                        .Select(s => s.ComputerName)
                        .ToListAsync();

                    result.AddRange(expired.Select(pc => new ClubPc(clubId, pc)));
                }
                catch (Exception ex)
                {
                    // База одного клуба недоступна — остальные обслуживаем как обычно.
                    Console.WriteLine($"[SessionManager] Клуб {clubId}: {ex.Message}");
                }
            }

            return result;
        }

        public async Task<Session?> GetActiveSessionAsync(int clubId, string pcName)
        {
            await using var db = _clubDb.Create(clubId);

            return await db.Sessions
                .Where(s => s.ComputerName == pcName && s.IsActive)
                .OrderByDescending(s => s.StartTime)
                .FirstOrDefaultAsync();
        }
    }
}
