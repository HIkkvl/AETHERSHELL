using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.DTOs;
using AetherShell.Server.Filters;
using AetherShell.Server.Models;
using AetherShell.Server.Services;

namespace AetherShell.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    [RequireClub]
    public class BannersController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly AuditLogger _logger;
        private readonly ClubRealtimeNotifier _live;
        private readonly ICurrentClub _currentClub;

        public BannersController(ClubDbContext context, AuditLogger logger, ClubRealtimeNotifier live, ICurrentClub currentClub)
        {
            _context = context;
            _logger = logger;
            _live = live;
            _currentClub = currentClub;
        }

        /// <summary>Клуб текущего запроса. Гарантирован атрибутом <see cref="RequireClubAttribute"/>.</summary>
        private int ClubId => _currentClub.ClubId!.Value;

        private Task NotifyBannersUpdatedAsync() => _live.BannersUpdatedAsync();

        [HttpGet]
        [AllowAnonymous] 
        public async Task<ActionResult<IEnumerable<BannerDto>>> GetBanners([FromQuery] bool activeOnly = true)
        {
            var query = _context.Banners.AsQueryable();

            if (activeOnly)
            {
                query = query.Where(b => b.IsActive);
            }

            var banners = await query
                .OrderBy(b => b.Position)
                .Select(b => new BannerDto
                {
                    Id = b.Id,
                    Title = b.Title,
                    ImageUrl = b.ImageUrl,
                    ClickUrl = b.ClickUrl,
                    Position = b.Position,
                    IsActive = b.IsActive
                })
                .ToListAsync();

            return Ok(banners);
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<ActionResult<BannerDto>> GetBanner(int id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null)
            {
                return NotFound();
            }

            var bannerDto = new BannerDto
            {
                Id = banner.Id,
                Title = banner.Title,
                ImageUrl = banner.ImageUrl,
                ClickUrl = banner.ClickUrl,
                Position = banner.Position,
                IsActive = banner.IsActive
            };

            return Ok(bannerDto);
        }

        [HttpPost]
        [Authorize(Roles = "Super")]
        public async Task<ActionResult<BannerDto>> CreateBanner(CreateBannerDto createDto)
        {
            if (createDto.Position < 1 || createDto.Position > 2)
            {
                return BadRequest("Position must be 1 (left) or 2 (right)");
            }

            var banner = new Banner
            {
                Title = createDto.Title,
                ImageUrl = createDto.ImageUrl,
                ClickUrl = createDto.ClickUrl,
                Position = createDto.Position,
                IsActive = createDto.IsActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Banners.Add(banner);
            await _context.SaveChangesAsync();

            var username = User.Identity?.Name ?? "Unknown";
            await _logger.LogAsync(username, "Banners", "Create", $"Создан баннер: {banner.Title} (позиция {banner.Position})");

            await NotifyBannersUpdatedAsync();

            var bannerDto = new BannerDto
            {
                Id = banner.Id,
                Title = banner.Title,
                ImageUrl = banner.ImageUrl,
                ClickUrl = banner.ClickUrl,
                Position = banner.Position,
                IsActive = banner.IsActive
            };

            return CreatedAtAction(nameof(GetBanner), new { id = banner.Id }, bannerDto);
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Super")]
        public async Task<IActionResult> UpdateBanner(int id, UpdateBannerDto updateDto)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null)
            {
                return NotFound();
            }

            if (updateDto.Position.HasValue && (updateDto.Position < 1 || updateDto.Position > 2))
            {
                return BadRequest("Position must be 1 (left) or 2 (right)");
            }

            if (!string.IsNullOrEmpty(updateDto.Title))
                banner.Title = updateDto.Title;
            if (!string.IsNullOrEmpty(updateDto.ImageUrl))
                banner.ImageUrl = updateDto.ImageUrl;
            if (!string.IsNullOrEmpty(updateDto.ClickUrl))
                banner.ClickUrl = updateDto.ClickUrl;
            if (updateDto.Position.HasValue)
                banner.Position = updateDto.Position.Value;
            if (updateDto.IsActive.HasValue)
                banner.IsActive = updateDto.IsActive.Value;

            banner.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var username = User.Identity?.Name ?? "Unknown";
            await _logger.LogAsync(username, "Banners", "Update", $"Обновлен баннер: {banner.Title} (ID: {id})");

            await NotifyBannersUpdatedAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Super")]
        public async Task<IActionResult> DeleteBanner(int id)
        {
            var banner = await _context.Banners.FindAsync(id);
            if (banner == null)
            {
                return NotFound();
            }

            var title = banner.Title;
            _context.Banners.Remove(banner);
            await _context.SaveChangesAsync();

            var username = User.Identity?.Name ?? "Unknown";
            await _logger.LogAsync(username, "Banners", "Delete", $"Удален баннер: {title} (ID: {id})");

            await NotifyBannersUpdatedAsync();

            return NoContent();
        }
    }
}
