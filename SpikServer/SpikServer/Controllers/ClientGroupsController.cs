using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Controllers
{
    /// <summary>Группы посетителей сети: VIP, скидки и произвольные метки.</summary>
    [ApiController]
    [Route("api/ClientGroups")]
    [Authorize(Roles = "Admin,Senior,Super")]
    [RequireClub]
    public class ClientGroupsController : ControllerBase
    {
        private readonly PlatformDbContext _platform;
        private readonly AuditLogger _logger;
        private readonly ClubRealtimeNotifier _live;
        private readonly ICurrentClub _currentClub;

        public ClientGroupsController(
            PlatformDbContext platform,
            AuditLogger logger,
            ClubRealtimeNotifier live,
            ICurrentClub currentClub)
        {
            _platform = platform;
            _logger = logger;
            _live = live;
            _currentClub = currentClub;
        }

        private bool HasNetwork => _currentClub.NetworkId != null;
        private int NetworkId => _currentClub.NetworkId!.Value;

        [HttpGet]
        public async Task<IActionResult> List()
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            var groups = await _platform.ClientGroups
                .AsNoTracking()
                .Where(g => g.NetworkId == NetworkId)
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Name)
                .Select(g => new
                {
                    g.Id,
                    g.Name,
                    g.Color,
                    g.DiscountPercent,
                    g.SortOrder,
                    clientsCount = _platform.Clients.Count(c => c.GroupId == g.Id)
                })
                .ToListAsync();

            return Ok(groups);
        }

        public class UpsertGroupDto
        {
            public string Name { get; set; } = "";
            public string? Color { get; set; }
            public int? DiscountPercent { get; set; }
            public int? SortOrder { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] UpsertGroupDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Название обязательно");

            var name = request.Name.Trim();
            if (await _platform.ClientGroups.AnyAsync(g => g.NetworkId == NetworkId && g.Name == name))
                return BadRequest("Группа с таким названием уже есть");

            var club = await _platform.Clubs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == _currentClub.ClubId);
            var maxDiscount = club?.MaxDiscountPercent ?? 20;
            if (request.DiscountPercent is int d && (d < 0 || d > maxDiscount))
                return BadRequest($"Скидка группы от 0 до {maxDiscount}%");

            var group = new ClientGroup
            {
                NetworkId = NetworkId,
                Name = name,
                Color = NormalizeColor(request.Color),
                DiscountPercent = request.DiscountPercent,
                SortOrder = request.SortOrder ?? 0
            };

            _platform.ClientGroups.Add(group);
            await _platform.SaveChangesAsync();

            var admin = User.Identity?.Name ?? "Admin";
            await _logger.LogAsync(admin, "UserMgmt", group.Name, "Создана группа клиентов");
            await _live.ClientsUpdatedAsync();

            return Ok(new
            {
                group.Id,
                group.Name,
                group.Color,
                group.DiscountPercent,
                group.SortOrder,
                clientsCount = 0
            });
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] UpsertGroupDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            var group = await _platform.ClientGroups
                .FirstOrDefaultAsync(g => g.Id == id && g.NetworkId == NetworkId);
            if (group == null) return NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Название обязательно");

            var name = request.Name.Trim();
            if (await _platform.ClientGroups.AnyAsync(g =>
                    g.NetworkId == NetworkId && g.Name == name && g.Id != id))
                return BadRequest("Группа с таким названием уже есть");

            var club = await _platform.Clubs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == _currentClub.ClubId);
            var maxDiscount = club?.MaxDiscountPercent ?? 20;
            if (request.DiscountPercent is int d && (d < 0 || d > maxDiscount))
                return BadRequest($"Скидка группы от 0 до {maxDiscount}%");

            group.Name = name;
            group.Color = NormalizeColor(request.Color ?? group.Color);
            group.DiscountPercent = request.DiscountPercent;
            if (request.SortOrder.HasValue) group.SortOrder = request.SortOrder.Value;

            await _platform.SaveChangesAsync();

            var admin = User.Identity?.Name ?? "Admin";
            await _logger.LogAsync(admin, "UserMgmt", group.Name, "Обновлена группа клиентов");
            await _live.ClientsUpdatedAsync();

            return Ok(new
            {
                group.Id,
                group.Name,
                group.Color,
                group.DiscountPercent,
                group.SortOrder
            });
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен");

            var group = await _platform.ClientGroups
                .FirstOrDefaultAsync(g => g.Id == id && g.NetworkId == NetworkId);
            if (group == null) return NotFound();

            var name = group.Name;
            // Клиенты отвяжутся через SetNull на FK.
            _platform.ClientGroups.Remove(group);
            await _platform.SaveChangesAsync();

            var admin = User.Identity?.Name ?? "Admin";
            await _logger.LogAsync(admin, "UserMgmt", name, "Удалена группа клиентов");
            await _live.ClientsUpdatedAsync();

            return Ok(new { message = "Удалено" });
        }

        private static string NormalizeColor(string? color)
        {
            if (string.IsNullOrWhiteSpace(color)) return "#6B7280";
            color = color.Trim();
            if (!color.StartsWith('#')) color = "#" + color;
            return color.Length <= 16 ? color : "#6B7280";
        }
    }
}
