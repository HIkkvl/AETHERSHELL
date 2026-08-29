using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Настройки клуба под клубным токеном. Те же поля правятся и через кабинет
    /// владельца (<c>PUT /api/clubs/{id}</c>), но у управляющего зала токена
    /// кабинета нет, а настраивать лояльность ему нужно.
    /// </summary>
    [Route("api/Club")]
    [ApiController]
    [Authorize(Roles = "Super")]
    [RequireClub]
    public class ClubSettingsController : ControllerBase
    {
        private readonly PlatformDbContext _platform;
        private readonly ICurrentClub _currentClub;
        private readonly AuditLogger _logger;
        private readonly ClubRealtimeNotifier _live;

        public ClubSettingsController(
            PlatformDbContext platform,
            ICurrentClub currentClub,
            AuditLogger logger,
            ClubRealtimeNotifier live)
        {
            _platform = platform;
            _currentClub = currentClub;
            _logger = logger;
            _live = live;
        }

        private int ClubId => _currentClub.ClubId!.Value;

        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings()
        {
            var club = await _platform.Clubs.FirstOrDefaultAsync(c => c.Id == ClubId);
            if (club == null) return NotFound("Клуб не найден");

            return Ok(new
            {
                club.Id,
                club.Name,
                club.City,
                club.Address,
                club.LoyaltyFirstThreshold,
                club.LoyaltyStep,
                club.MaxDiscountPercent,
                club.RequireComputerApproval,
                club.EnableShop
            });
        }

        [HttpPut("settings")]
        public async Task<IActionResult> UpdateSettings([FromBody] ClubSettingsDto dto)
        {
            if (dto.LoyaltyFirstThreshold <= 0)
                return BadRequest("Первый порог должен быть больше нуля");
            if (dto.LoyaltyStep < 0)
                return BadRequest("Шаг не может быть отрицательным");
            if (dto.MaxDiscountPercent is < 0 or > 90)
                return BadRequest("Максимальная скидка должна быть от 0 до 90 процентов");

            var club = await _platform.Clubs.FirstOrDefaultAsync(c => c.Id == ClubId);
            if (club == null) return NotFound("Клуб не найден");

            club.LoyaltyFirstThreshold = dto.LoyaltyFirstThreshold;
            club.LoyaltyStep = dto.LoyaltyStep;
            club.MaxDiscountPercent = dto.MaxDiscountPercent;
            club.RequireComputerApproval = dto.RequireComputerApproval;
            club.EnableShop = dto.EnableShop;

            await _platform.SaveChangesAsync();

            var adminName = User.Identity?.Name ?? "Super";
            await _logger.LogAsync(adminName, "Settings", club.Name,
                $"Лояльность: первый порог {club.LoyaltyFirstThreshold}, шаг {club.LoyaltyStep}, максимум {club.MaxDiscountPercent}%, магазин {(club.EnableShop ? "вкл" : "выкл")}");
            await _live.LoyaltyUpdatedAsync();

            return Ok(new { message = "Настройки сохранены" });
        }

        /// <summary>
        /// Клиенты по накопленным тратам — кто в каком уровне скидки. Траты копятся
        /// по всей сети, поэтому список общий на филиалы, а пороги берутся у клуба.
        /// </summary>
        [HttpGet("loyalty-clients")]
        public async Task<IActionResult> GetLoyaltyClients()
        {
            var club = await _platform.Clubs.FirstOrDefaultAsync(c => c.Id == ClubId);

            var clients = await _platform.Clients
                .Include(c => c.Group)
                .Where(c => c.NetworkId == _currentClub.NetworkId)
                .OrderByDescending(c => c.TotalSpent)
                .Take(200)
                .ToListAsync();

            return Ok(clients.Select(c => new
            {
                c.Id,
                c.Username,
                c.Balance,
                c.TotalSpent,
                DiscountPercent = Loyalty.EffectiveDiscount(c, club),
                NextThreshold = Loyalty.NextThreshold(c, club)
            }));
        }
    }

    public class ClubSettingsDto
    {
        public decimal LoyaltyFirstThreshold { get; set; }
        public decimal LoyaltyStep { get; set; }
        public int MaxDiscountPercent { get; set; }
        public bool RequireComputerApproval { get; set; }
        public bool EnableShop { get; set; } = true;
    }
}
