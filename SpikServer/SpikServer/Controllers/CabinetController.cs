using AetherShell.Server.Data;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// API личного кабинета владельца сети и платформенного админа.
    /// Работает на account-токене без X-Club-Id: сводка и клиенты сети
    /// собираются по всем доступным филиалам.
    /// </summary>
    [ApiController]
    [Route("api/cabinet")]
    [Authorize]
    public class CabinetController : ControllerBase
    {
        private readonly PlatformDbContext _platform;
        private readonly ICurrentClub _currentClub;
        private readonly IClubDbContextFactory _clubDb;

        public CabinetController(
            PlatformDbContext platform,
            ICurrentClub currentClub,
            IClubDbContextFactory clubDb)
        {
            _platform = platform;
            _currentClub = currentClub;
            _clubDb = clubDb;
        }

        private bool IsPlatformAdmin => _currentClub.IsPlatformAdmin;
        private int? AccountId => _currentClub.AccountId;

        /// <summary>
        /// Сводка по всем доступным клубам: ПК, сессии, выручка, клиенты сети.
        /// Owner видит свою сеть, PlatformAdmin — всю платформу.
        /// </summary>
        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary(CancellationToken ct)
        {
            if (AccountId == null) return Unauthorized();

            var clubsQuery = _platform.Clubs.AsNoTracking();
            if (!IsPlatformAdmin)
                clubsQuery = clubsQuery.Where(c => c.OwnerId == AccountId);

            var clubs = await clubsQuery
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.City,
                    c.Address,
                    c.IsActive,
                    c.OwnerId,
                    OwnerEmail = c.Owner.Email
                })
                .ToListAsync(ct);

            var todayStart = DateTime.UtcNow.Date;
            var weekStart = todayStart.AddDays(-6);

            var clubRows = new List<object>(clubs.Count);
            int totalPcs = 0, onlinePcs = 0, pendingPcs = 0;
            int sessionsToday = 0, activeSessions = 0;
            decimal revenueToday = 0, revenueWeek = 0;

            // Клиентов считаем по уникальным сетям (OwnerId), а не по числу клубов:
            // у сети из нескольких филиалов клиенты общие.
            var networkIds = clubs.Select(c => c.OwnerId).Distinct().ToList();
            var clientsByNetwork = await _platform.Clients.AsNoTracking()
                .Where(c => networkIds.Contains(c.NetworkId))
                .GroupBy(c => c.NetworkId)
                .Select(g => new { NetworkId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.NetworkId, x => x.Count, ct);

            var totalClients = clientsByNetwork.Values.Sum();

            foreach (var club in clubs)
            {
                var metrics = await LoadClubMetricsAsync(club.Id, todayStart, weekStart, ct);
                clientsByNetwork.TryGetValue(club.OwnerId, out var networkClients);

                totalPcs += metrics.ComputersTotal;
                onlinePcs += metrics.ComputersOnline;
                pendingPcs += metrics.ComputersPending;
                sessionsToday += metrics.SessionsToday;
                activeSessions += metrics.ActiveSessions;
                revenueToday += metrics.RevenueToday;
                revenueWeek += metrics.RevenueWeek;

                clubRows.Add(new
                {
                    club.Id,
                    club.Name,
                    club.City,
                    club.Address,
                    club.IsActive,
                    club.OwnerId,
                    club.OwnerEmail,
                    networkClients,
                    computersTotal = metrics.ComputersTotal,
                    computersOnline = metrics.ComputersOnline,
                    computersPending = metrics.ComputersPending,
                    activeSessions = metrics.ActiveSessions,
                    sessionsToday = metrics.SessionsToday,
                    revenueToday = metrics.RevenueToday,
                    revenueWeek = metrics.RevenueWeek
                });
            }

            return Ok(new
            {
                role = IsPlatformAdmin ? AccountRoles.PlatformAdmin : AccountRoles.Owner,
                totals = new
                {
                    clubs = clubs.Count,
                    clients = totalClients,
                    computersTotal = totalPcs,
                    computersOnline = onlinePcs,
                    computersPending = pendingPcs,
                    activeSessions,
                    sessionsToday,
                    revenueToday,
                    revenueWeek
                },
                clubs = clubRows
            });
        }

        /// <summary>
        /// Клиенты сети. Owner всегда видит свою сеть.
        /// PlatformAdmin обязан указать clubId — сеть берётся у владельца этого клуба.
        /// </summary>
        [HttpGet("clients")]
        public async Task<IActionResult> GetClients(
            [FromQuery] string? search = null,
            [FromQuery] int? clubId = null,
            [FromQuery] int? networkId = null,
            CancellationToken ct = default)
        {
            if (AccountId == null) return Unauthorized();

            var resolvedNetworkId = await ResolveNetworkIdAsync(clubId, networkId, ct);
            if (resolvedNetworkId == null)
            {
                return BadRequest(new
                {
                    error = IsPlatformAdmin
                        ? "Укажите clubId или networkId — клиентов всей платформы одним списком не отдаём"
                        : "Сеть не найдена"
                });
            }

            var query = _platform.Clients.AsNoTracking()
                .Where(c => c.NetworkId == resolvedNetworkId);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Username.Contains(search) || c.Email.Contains(search));

            var clients = await query
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Username,
                    c.Email,
                    c.Balance,
                    c.TotalSpent,
                    c.RemainingMinutes,
                    c.CreatedAt,
                    c.RegisteredClubId
                })
                .Take(500)
                .ToListAsync(ct);

            // Где сидит клиент — смотрим по всем филиалам этой сети.
            var networkClubIds = await _platform.Clubs.AsNoTracking()
                .Where(c => c.OwnerId == resolvedNetworkId && (IsPlatformAdmin || c.OwnerId == AccountId))
                .Select(c => c.Id)
                .ToListAsync(ct);

            var usernames = clients.Select(c => c.Username).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var occupancy = await FindOccupancyAsync(networkClubIds, usernames, ct);

            var result = clients.Select(c =>
            {
                occupancy.TryGetValue(c.Username, out var pc);
                return new
                {
                    c.Id,
                    c.Username,
                    c.Email,
                    c.Balance,
                    c.TotalSpent,
                    c.RemainingMinutes,
                    c.CreatedAt,
                    c.RegisteredClubId,
                    currentClubId = pc?.ClubId,
                    currentClubName = pc?.ClubName,
                    currentPcName = pc?.Name,
                    currentPcDisplay = pc?.Display
                };
            });

            return Ok(new
            {
                networkId = resolvedNetworkId,
                clients = result
            });
        }

        /// <summary>
        /// Карточка клиента сети: баланс общий, история — из выбранного филиала.
        /// </summary>
        [HttpGet("clients/{id:int}")]
        public async Task<IActionResult> GetClientProfile(
            int id,
            [FromQuery] int? clubId = null,
            CancellationToken ct = default)
        {
            if (AccountId == null) return Unauthorized();

            var client = await _platform.Clients.AsNoTracking()
                .Include(c => c.Group)
                .FirstOrDefaultAsync(c => c.Id == id, ct);
            if (client == null) return NotFound(new { error = "Клиент не найден" });

            if (!IsPlatformAdmin && client.NetworkId != AccountId)
                return NotFound(new { error = "Клиент не найден" });

            // Филиал для истории: явно выбранный, иначе первый клуб сети.
            var historyClub = await ResolveHistoryClubAsync(client.NetworkId, clubId, ct);
            if (historyClub == null)
                return BadRequest(new { error = "Нет доступного клуба для истории" });

            if (!IsPlatformAdmin && historyClub.OwnerId != AccountId)
                return Forbid();

            var discountPercent = Loyalty.EffectiveDiscount(client, historyClub);
            var nextThreshold = Loyalty.NextThreshold(client, historyClub);

            var sessions = new List<object>();
            var orders = new List<object>();
            string? currentPcDisplay = null;
            string? currentApp = null;
            DateTime? sessionEndTime = null;

            try
            {
                await using var db = _clubDb.Create(historyClub.Id);

                var pc = await db.Computers
                    .Where(c => c.CurrentUser == client.Username)
                    .Select(c => new { c.Name, c.DisplayName, c.SessionEndTime, c.CurrentApp })
                    .FirstOrDefaultAsync(ct);

                if (pc != null)
                {
                    currentPcDisplay = string.IsNullOrEmpty(pc.DisplayName) ? pc.Name : pc.DisplayName;
                    currentApp = pc.CurrentApp;
                    sessionEndTime = pc.SessionEndTime;
                }

                var sessionRows = await db.Sessions
                    .Where(s => s.Username == client.Username)
                    .OrderByDescending(s => s.StartTime)
                    .Take(50)
                    .Select(s => new
                    {
                        s.Id,
                        s.ComputerName,
                        s.StartTime,
                        s.EndTime,
                        s.IsActive,
                        s.Price
                    })
                    .ToListAsync(ct);
                sessions = sessionRows.Cast<object>().ToList();

                var orderRows = await db.Orders
                    .Where(o => o.ClientId == client.Id)
                    .OrderByDescending(o => o.CreatedAt)
                    .Take(50)
                    .Select(o => new
                    {
                        o.Id,
                        o.PcName,
                        o.TotalPrice,
                        Status = o.Status.ToString(),
                        o.CreatedAt,
                        Items = o.Items.Select(i => new { i.ProductNameSnapshot, i.Quantity }).ToList()
                    })
                    .ToListAsync(ct);
                orders = orderRows.Cast<object>().ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cabinet] История клиента {id} в клубе {historyClub.Id}: {ex.Message}");
            }

            var networkClubs = await _platform.Clubs.AsNoTracking()
                .Where(c => c.OwnerId == client.NetworkId && c.IsActive)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync(ct);

            return Ok(new
            {
                client.Id,
                client.Username,
                client.Email,
                client.Balance,
                client.RemainingMinutes,
                client.TotalSpent,
                client.CreatedAt,
                discountPercent,
                nextThreshold,
                maxDiscountPercent = historyClub.MaxDiscountPercent,
                networkId = client.NetworkId,
                networkClubs,
                historyClubId = historyClub.Id,
                historyClubName = historyClub.Name,
                currentPcDisplay,
                currentApp,
                sessionEndTime,
                sessions,
                orders
            });
        }

        private async Task<int?> ResolveNetworkIdAsync(int? clubId, int? networkId, CancellationToken ct)
        {
            if (!IsPlatformAdmin)
                return AccountId;

            if (networkId is int nid && nid > 0)
            {
                var exists = await _platform.Clubs.AsNoTracking()
                    .AnyAsync(c => c.OwnerId == nid, ct);
                return exists ? nid : null;
            }

            if (clubId is int cid && cid > 0)
            {
                return await _platform.Clubs.AsNoTracking()
                    .Where(c => c.Id == cid)
                    .Select(c => (int?)c.OwnerId)
                    .FirstOrDefaultAsync(ct);
            }

            return null;
        }

        private async Task<Club?> ResolveHistoryClubAsync(int networkId, int? clubId, CancellationToken ct)
        {
            var query = _platform.Clubs.AsNoTracking()
                .Where(c => c.OwnerId == networkId);

            if (!IsPlatformAdmin)
                query = query.Where(c => c.OwnerId == AccountId);

            if (clubId is int cid && cid > 0)
            {
                return await query.FirstOrDefaultAsync(c => c.Id == cid, ct);
            }

            return await query.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
        }

        private async Task<ClubMetrics> LoadClubMetricsAsync(
            int clubId, DateTime todayStart, DateTime weekStart, CancellationToken ct)
        {
            try
            {
                await using var db = _clubDb.Create(clubId);
                var total = await db.Computers.CountAsync(x => x.IsApproved, ct);
                var online = await db.Computers.CountAsync(x => x.IsOnline && x.IsApproved, ct);
                var pending = await db.Computers.CountAsync(x => !x.IsApproved, ct);
                var activeSessions = await db.Sessions.CountAsync(s => s.IsActive && s.EndTime > DateTime.UtcNow, ct);
                var sessionsToday = await db.Sessions.CountAsync(s => s.StartTime >= todayStart, ct);

                var revenueToday = await db.Orders
                    .Where(o => o.CreatedAt >= todayStart && o.Status != OrderStatus.Cancelled)
                    .SumAsync(o => (decimal?)o.TotalPrice, ct) ?? 0;

                var revenueWeek = await db.Orders
                    .Where(o => o.CreatedAt >= weekStart && o.Status != OrderStatus.Cancelled)
                    .SumAsync(o => (decimal?)o.TotalPrice, ct) ?? 0;

                return new ClubMetrics(total, online, pending, activeSessions, sessionsToday, revenueToday, revenueWeek);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cabinet] Метрики клуба {clubId}: {ex.Message}");
                return ClubMetrics.Empty;
            }
        }

        private async Task<Dictionary<string, Occupancy>> FindOccupancyAsync(
            IReadOnlyList<int> clubIds,
            HashSet<string> usernames,
            CancellationToken ct)
        {
            var result = new Dictionary<string, Occupancy>(StringComparer.OrdinalIgnoreCase);
            if (usernames.Count == 0 || clubIds.Count == 0) return result;

            var clubNames = await _platform.Clubs.AsNoTracking()
                .Where(c => clubIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

            foreach (var clubId in clubIds)
            {
                try
                {
                    await using var db = _clubDb.Create(clubId);
                    var occupied = await db.Computers
                        .Where(pc => pc.IsOnline && pc.CurrentUser != null)
                        .Select(pc => new { pc.Name, pc.DisplayName, pc.CurrentUser })
                        .ToListAsync(ct);

                    clubNames.TryGetValue(clubId, out var clubName);

                    foreach (var pc in occupied)
                    {
                        if (pc.CurrentUser == null || !usernames.Contains(pc.CurrentUser))
                            continue;
                        if (result.ContainsKey(pc.CurrentUser))
                            continue;

                        result[pc.CurrentUser] = new Occupancy(
                            clubId,
                            clubName ?? $"#{clubId}",
                            pc.Name,
                            string.IsNullOrEmpty(pc.DisplayName) ? pc.Name : pc.DisplayName);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Cabinet] Occupancy клуба {clubId}: {ex.Message}");
                }
            }

            return result;
        }

        private sealed record ClubMetrics(
            int ComputersTotal,
            int ComputersOnline,
            int ComputersPending,
            int ActiveSessions,
            int SessionsToday,
            decimal RevenueToday,
            decimal RevenueWeek)
        {
            public static ClubMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);
        }

        private sealed record Occupancy(int ClubId, string ClubName, string Name, string Display);
    }
}
