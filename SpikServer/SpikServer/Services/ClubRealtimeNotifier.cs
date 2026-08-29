using AetherShell.Server.Constants;
using AetherShell.Server.Data;
using AetherShell.Server.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Рассылка клубных обновлений в шелл (группы ПК) и админ-панель (группа admins).
    /// Каталоги, клиенты, ПК — всё, что должно меняться «сразу», без перезагрузки.
    /// </summary>
    public sealed class ClubRealtimeNotifier
    {
        private readonly IHubContext<ClubHub> _hub;
        private readonly ICurrentClub _currentClub;

        public ClubRealtimeNotifier(IHubContext<ClubHub> hub, ICurrentClub currentClub)
        {
            _hub = hub;
            _currentClub = currentClub;
        }

        public Task AppsUpdatedAsync() => NotifyClubAsync(SignalRMethods.AppsUpdated);
        public Task ProductsUpdatedAsync() => NotifyClubAsync(SignalRMethods.ProductsUpdated);
        public Task TariffsUpdatedAsync() => NotifyClubAsync(SignalRMethods.TariffsUpdated);
        public Task BannersUpdatedAsync() => NotifyClubAsync(SignalRMethods.BannersUpdated);
        public Task ClientsUpdatedAsync() => NotifyClubAsync(SignalRMethods.ClientsUpdated);
        public Task ComputersUpdatedAsync() => NotifyClubAsync(SignalRMethods.ComputersUpdated);
        public Task LoyaltyUpdatedAsync() => NotifyClubAsync(SignalRMethods.LoyaltyUpdated);

        /// <summary>Пинг дашборда админов (онлайн/офлайн, текущее приложение).</summary>
        public Task DashboardUpdatedAsync()
        {
            if (_currentClub.ClubId is not int clubId) return Task.CompletedTask;
            return _hub.Clients.Group(ClubHub.AdminGroup(clubId)).SendAsync("DashboardUpdate");
        }

        public Task NotifyClubAsync(string method)
        {
            if (_currentClub.ClubId is not int clubId) return Task.CompletedTask;

            var groups = ClubHub.OnlinePcIds(clubId)
                .Select(pc => ClubHub.PcGroup(clubId, pc))
                .Append(ClubHub.AdminGroup(clubId))
                .ToList();

            if (groups.Count == 0)
                groups.Add(ClubHub.AdminGroup(clubId));

            return _hub.Clients.Groups(groups).SendAsync(method);
        }

        /// <summary>Когда клуб известен явно (фоновые сервисы / хаб без ICurrentClub).</summary>
        public Task NotifyClubAsync(int clubId, string method)
        {
            var groups = ClubHub.OnlinePcIds(clubId)
                .Select(pc => ClubHub.PcGroup(clubId, pc))
                .Append(ClubHub.AdminGroup(clubId))
                .ToList();

            return _hub.Clients.Groups(groups).SendAsync(method);
        }
    }
}
