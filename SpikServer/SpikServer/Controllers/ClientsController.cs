using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.DTOs;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Посетители сети клубов. Живут в платформенной базе: баланс, минуты и накопленные
    /// траты общие на все филиалы одного владельца, поэтому пополнение в одном зале
    /// сразу видно в другом.
    ///
    /// История (сессии, заказы, логи) остаётся данными конкретного зала, поэтому в
    /// карточке показывается история того клуба, из которого открыли панель.
    ///
    /// Персонал сюда не попадает — он привязан к своему клубу, см. <see cref="UsersController"/>.
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    [AetherShell.Server.Filters.RequireClub]
    public class ClientsController : ControllerBase
    {
        private readonly ClubDbContext _club;
        private readonly PlatformDbContext _platform;
        private readonly AuditLogger _logger;
        private readonly BalanceNotifier _balance;
        private readonly ClubRealtimeNotifier _live;
        private readonly ICurrentClub _currentClub;

        public ClientsController(
            ClubDbContext club,
            PlatformDbContext platform,
            AuditLogger logger,
            BalanceNotifier balance,
            ClubRealtimeNotifier live,
            ICurrentClub currentClub)
        {
            _club = club;
            _platform = platform;
            _logger = logger;
            _balance = balance;
            _live = live;
            _currentClub = currentClub;
        }

        private IQueryable<Client> NetworkClients => _platform.Clients.Where(c => c.NetworkId == _currentClub.NetworkId);

        private bool HasNetwork => _currentClub.NetworkId != null;

        [Authorize(Roles = "Admin,Senior,Super")]
        [HttpGet]
        public async Task<IActionResult> GetClients(
            [FromQuery] string? search = null,
            [FromQuery] int? groupId = null,
            [FromQuery] bool ungrouped = false)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            var query = NetworkClients.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(c => c.Username.Contains(search));
            if (ungrouped)
                query = query.Where(c => c.GroupId == null);
            else if (groupId.HasValue)
                query = query.Where(c => c.GroupId == groupId.Value);

            var clients = await query
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Username,
                    c.Email,
                    c.Balance,
                    c.CreatedAt,
                    c.GroupId,
                    GroupName = c.Group != null ? c.Group.Name : null,
                    GroupColor = c.Group != null ? c.Group.Color : null
                })
                .ToListAsync();

            // За каким ПК сидит посетитель, знает база клуба, а сами посетители лежат
            // в платформенной: соединить одним запросом нельзя, сопоставляем по логину.
            var usernames = clients.Select(c => c.Username).ToList();
            var occupied = await _club.Computers
                .Where(pc => pc.IsOnline && pc.CurrentUser != null && usernames.Contains(pc.CurrentUser))
                .Select(pc => new { pc.Name, pc.DisplayName, pc.CurrentUser })
                .ToListAsync();

            var byUser = occupied
                .GroupBy(pc => pc.CurrentUser!)
                .ToDictionary(g => g.Key, g => g.First());

            var result = clients.Select(c =>
            {
                byUser.TryGetValue(c.Username, out var pc);
                return new
                {
                    c.Id,
                    c.Username,
                    c.Email,
                    c.Balance,
                    Role = "Client",
                    c.CreatedAt,
                    c.GroupId,
                    c.GroupName,
                    c.GroupColor,
                    CurrentPcName = pc?.Name,
                    CurrentPcDisplay = pc == null ? null : (string.IsNullOrEmpty(pc.DisplayName) ? pc.Name : pc.DisplayName)
                };
            });

            return Ok(result);
        }

        /// <summary>
        /// Полная карточка посетителя для боковой панели: баланс сети, лояльность,
        /// история сессий, заказов и действий администраторов в этом зале.
        /// </summary>
        [Authorize(Roles = "Admin,Senior,Super")]
        [HttpGet("{id}/profile")]
        public async Task<IActionResult> GetProfile(int id)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            var client = await NetworkClients
                .Include(c => c.Group)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound("Клиент не найден");

            var club = _currentClub.ClubId is int clubId
                ? await _platform.Clubs.FirstOrDefaultAsync(c => c.Id == clubId)
                : null;

            // Сколько филиалов делят этот баланс — панель показывает это в карточке.
            var networkClubs = await _platform.Clubs
                .CountAsync(c => c.OwnerId == _currentClub.NetworkId && c.IsActive);

            var pc = await _club.Computers
                .Where(c => c.CurrentUser == client.Username)
                .Select(c => new { c.Name, c.DisplayName, c.SessionEndTime, c.CurrentApp })
                .FirstOrDefaultAsync();

            var sessionRows = await _club.Sessions
                .AsNoTracking()
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
                .ToListAsync();

            var pcNames = sessionRows.Select(s => s.ComputerName).Distinct().ToList();
            var displayByName = await _club.Computers
                .AsNoTracking()
                .Where(c => pcNames.Contains(c.Name))
                .Select(c => new { c.Name, c.DisplayName })
                .ToDictionaryAsync(
                    c => c.Name,
                    c => string.IsNullOrEmpty(c.DisplayName) ? c.Name : c.DisplayName);

            var sessions = sessionRows.Select(s => new
            {
                s.Id,
                ComputerName = displayByName.TryGetValue(s.ComputerName, out var title) ? title : s.ComputerName,
                s.StartTime,
                s.EndTime,
                s.IsActive,
                s.Price
            }).ToList();

            var orders = await _club.Orders
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
                .ToListAsync();

            // Логи пишутся с разным Target: где-то чистый логин, где-то "User: логин".
            var targetSuffix = $": {client.Username}";
            var logs = await _club.AdminLogs
                .Where(l => l.Target == client.Username || l.Target.EndsWith(targetSuffix))
                .OrderByDescending(l => l.CreatedAt)
                .Take(50)
                .Select(l => new
                {
                    l.Id,
                    l.AdminName,
                    l.ActionType,
                    l.Details,
                    l.CreatedAt
                })
                .ToListAsync();

            // Скидка: ручная корректировка или расчёт из трат по сети.
            var discountPercent = Loyalty.EffectiveDiscount(client, club);
            var nextThreshold = Loyalty.NextThreshold(client, club);

            return Ok(new
            {
                client.Id,
                client.Username,
                client.Email,
                Role = "Client",
                client.Balance,
                client.RemainingMinutes,
                client.TotalSpent,
                client.CreatedAt,
                client.GroupId,
                GroupName = client.Group?.Name,
                GroupColor = client.Group?.Color,
                GroupDiscountPercent = client.Group?.DiscountPercent,
                DiscountPercent = discountPercent,
                DiscountOverride = client.DiscountOverride,
                NextThreshold = nextThreshold,
                MaxDiscountPercent = club?.MaxDiscountPercent ?? 20,
                NetworkClubs = networkClubs,
                CurrentPcName = pc?.Name,
                CurrentPcDisplay = string.IsNullOrEmpty(pc?.DisplayName) ? pc?.Name : pc.DisplayName,
                CurrentApp = pc?.CurrentApp,
                SessionEndTime = pc?.SessionEndTime,
                TotalSessions = sessions.Count,
                Sessions = sessions,
                Orders = orders,
                Logs = logs
            });
        }

        [HttpPost("create")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> CreateClient([FromBody] CreateUserDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Заполните логин и пароль");

            if (await NetworkClients.AnyAsync(c => c.Username == request.Username))
                return BadRequest("Клиент с таким логином уже есть в сети");

            if (await _club.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest("Этот логин занят сотрудником зала");

            var client = new Client
            {
                NetworkId = _currentClub.NetworkId!.Value,
                Username = request.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Email = request.Email ?? "",
                Balance = request.Balance,
                RemainingMinutes = 0,
                CreatedAt = DateTime.UtcNow,
                RegisteredClubId = _currentClub.ClubId
            };

            _platform.Clients.Add(client);
            await _platform.SaveChangesAsync();

            var adminName = User.Identity?.Name ?? "Admin";
            await _logger.LogAsync(adminName, "UserMgmt", request.Username, "Создан клиент сети");
            await _live.ClientsUpdatedAsync();

            return Ok(new { message = "Клиент создан", id = client.Id });
        }

        /// <summary>
        /// Удаление затрагивает всю сеть: клиент исчезнет и в остальных филиалах вместе
        /// с балансом. Поэтому доступно только управляющему.
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Roles = "Super")]
        public async Task<IActionResult> DeleteClient(int id)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            var client = await NetworkClients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();

            var username = client.Username;
            _platform.Clients.Remove(client);
            await _platform.SaveChangesAsync();

            var adminName = User.Identity?.Name ?? "Admin";
            await _logger.LogAsync(adminName, "UserMgmt", username, "Клиент удалён из сети");
            await _live.ClientsUpdatedAsync();

            return Ok(new { message = "Удалено" });
        }

        [Authorize(Roles = "Admin,Senior,Super")]
        [HttpPost("{username}/topup")]
        public async Task<IActionResult> TopUpBalance(string username, [FromBody] decimal amount)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");
            if (amount <= 0) return BadRequest("Сумма должна быть > 0");

            var client = await NetworkClients.FirstOrDefaultAsync(c => c.Username == username);
            if (client == null) return NotFound("Клиент не найден");

            client.Balance += amount;
            await _platform.SaveChangesAsync();

            var adminName = User.Identity?.Name ?? "Unknown";
            await _logger.LogAsync(adminName, "Money", $"User: {username}", $"Пополнение: {amount} ₸");

            await _balance.PushAsync(client.Username, client.Balance);

            return Ok(new { message = "Баланс пополнен", newBalance = client.Balance });
        }

        public class AdjustClientWalletDto
        {
            public decimal? Balance { get; set; }
            public int? RemainingMinutes { get; set; }
            public int? DiscountPercent { get; set; }
            public int? GroupId { get; set; }
            public bool ClearGroup { get; set; }
        }

        /// <summary>
        /// Корректировка баланса, остатка минут, скидки и группы из карточки клиента.
        /// </summary>
        [Authorize(Roles = "Admin,Senior,Super")]
        [HttpPut("{id:int}/wallet")]
        public async Task<IActionResult> AdjustWallet(int id, [FromBody] AdjustClientWalletDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");
            if (request == null) return BadRequest("Пустой запрос");

            var client = await NetworkClients
                .Include(c => c.Group)
                .FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound("Клиент не найден");

            var club = await _platform.Clubs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == _currentClub.ClubId);
            var maxDiscount = club?.MaxDiscountPercent ?? 20;

            var changes = new List<string>();

            if (request.ClearGroup || request.GroupId.HasValue)
            {
                int? nextGroupId = request.ClearGroup ? null : request.GroupId;
                if (nextGroupId.HasValue)
                {
                    var exists = await _platform.ClientGroups.AnyAsync(g =>
                        g.Id == nextGroupId.Value && g.NetworkId == _currentClub.NetworkId);
                    if (!exists) return BadRequest("Группа не найдена");
                }

                if (client.GroupId != nextGroupId)
                {
                    var from = client.Group?.Name ?? "без группы";
                    client.GroupId = nextGroupId;
                    await _platform.Entry(client).Reference(c => c.Group).LoadAsync();
                    var to = client.Group?.Name ?? "без группы";
                    changes.Add($"группа {from} → {to}");
                }
            }

            if (request.Balance.HasValue)
            {
                if (request.Balance.Value < 0) return BadRequest("Баланс не может быть отрицательным");
                if (client.Balance != request.Balance.Value)
                {
                    changes.Add($"баланс {client.Balance:0.##} → {request.Balance.Value:0.##} ₸");
                    client.Balance = request.Balance.Value;
                }
            }

            if (request.RemainingMinutes.HasValue)
            {
                if (request.RemainingMinutes.Value < 0) return BadRequest("Минуты не могут быть отрицательными");
                if (client.RemainingMinutes != request.RemainingMinutes.Value)
                {
                    changes.Add($"минуты {client.RemainingMinutes} → {request.RemainingMinutes.Value}");
                    client.RemainingMinutes = request.RemainingMinutes.Value;
                }
            }

            if (request.DiscountPercent.HasValue)
            {
                var pct = request.DiscountPercent.Value;
                if (pct < 0) return BadRequest("Скидка не может быть отрицательной");
                if (pct > maxDiscount) return BadRequest($"Скидка не больше {maxDiscount}%");

                // Базовая скидка без ручного оверрайда (группа или лояльность).
                var prevOverride = client.DiscountOverride;
                client.DiscountOverride = null;
                var basePct = Loyalty.EffectiveDiscount(client, club);
                client.DiscountOverride = prevOverride;

                int? nextOverride = pct == basePct ? null : pct;
                if (client.DiscountOverride != nextOverride)
                {
                    var from = Loyalty.EffectiveDiscount(client, club);
                    client.DiscountOverride = nextOverride;
                    changes.Add($"скидка {from}% → {pct}%");
                }
            }

            if (changes.Count == 0)
                return Ok(new { message = "Изменений нет" });

            await _platform.SaveChangesAsync();

            var adminName = User.Identity?.Name ?? "Unknown";
            await _logger.LogAsync(adminName, "Money", $"User: {client.Username}",
                "Корректировка: " + string.Join("; ", changes));

            await _balance.PushAsync(client.Username, client.Balance);
            await _live.ClientsUpdatedAsync();

            return Ok(new
            {
                message = "Сохранено",
                balance = client.Balance,
                remainingMinutes = client.RemainingMinutes,
                discountPercent = Loyalty.EffectiveDiscount(client, club),
                discountOverride = client.DiscountOverride,
                groupId = client.GroupId,
                groupName = client.Group?.Name,
                groupColor = client.Group?.Color,
                totalSpent = client.TotalSpent
            });
        }
    }
}
