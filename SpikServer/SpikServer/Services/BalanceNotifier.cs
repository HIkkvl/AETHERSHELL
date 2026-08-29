using AetherShell.Server.Data;
using AetherShell.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Досылает клиенту новый баланс, чтобы в шелле он менялся сразу, а не после
    /// перелогина. ПК находится по <c>Computer.CurrentUser</c>: только там сидит
    /// этот человек прямо сейчас.
    /// </summary>
    public class BalanceNotifier
    {
        private readonly ClubDbContext _db;
        private readonly IHubContext<ClubHub> _hub;
        private readonly ICurrentClub _currentClub;
        private readonly ClubRealtimeNotifier _clubLive;

        public BalanceNotifier(
            ClubDbContext db,
            IHubContext<ClubHub> hub,
            ICurrentClub currentClub,
            ClubRealtimeNotifier clubLive)
        {
            _db = db;
            _hub = hub;
            _currentClub = currentClub;
            _clubLive = clubLive;
        }

        public async Task PushAsync(string? username, decimal balance)
        {
            if (string.IsNullOrEmpty(username)) return;
            if (_currentClub.ClubId is not int clubId) return;

            var pcNames = await _db.Computers
                .Where(c => c.CurrentUser == username)
                .Select(c => c.Name)
                .ToListAsync();

            foreach (var pcName in pcNames)
            {
                await _hub.Clients
                    .Group(ClubHub.PcGroup(clubId, pcName))
                    .SendAsync("BalanceUpdated", balance);
            }

            // Админ-панель обновляет список клиентов / карточку без F5.
            await _clubLive.ClientsUpdatedAsync();
        }
    }
}
