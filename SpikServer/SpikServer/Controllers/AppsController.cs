using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.Services;

namespace AetherShell.Server.Controllers
{
    // Список игр читает шелл под токеном обычного клиента, поэтому защита
    // стоит на изменяющих действиях, а не на всём контроллере (как у Tariffs/Products).
    [Route("api/[controller]")]
    [ApiController]
    [AetherShell.Server.Filters.RequireClub]
    public class AppsController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly ClubRealtimeNotifier _live;

        public AppsController(ClubDbContext context, ClubRealtimeNotifier live)
        {
            _context = context;
            _live = live;
        }

        // 1. ПОЛУЧИТЬ ВСЕ ИГРЫ
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetApps()
        {
            return Ok(await _context.AppItems.ToListAsync());
        }

        // 2. ДОБАВИТЬ ИГРУ
        [HttpPost]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> AddApp([FromBody] AppItem app)
        {
            if (string.IsNullOrEmpty(app.Title))
                return BadRequest("Название обязательно");

            _context.AppItems.Add(app);
            await _context.SaveChangesAsync();
            await _live.AppsUpdatedAsync();
            return Ok(app);
        }

        // 3. ОБНОВИТЬ ИГРУ
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> UpdateApp(int id, [FromBody] AppItem app)
        {
            if (id != app.Id) return BadRequest();

            var existing = await _context.AppItems.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Title = app.Title;
            existing.ExePath = app.ExePath;
            existing.ImageUrl = app.ImageUrl;
            existing.Category = app.Category; 
            existing.Arguments = app.Arguments;

            await _context.SaveChangesAsync();
            await _live.AppsUpdatedAsync();
            return Ok(new { message = "Обновлено" });
        }

        // 4. УДАЛИТЬ ИГРУ
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> DeleteApp(int id)
        {
            var app = await _context.AppItems.FindAsync(id);
            if (app == null) return NotFound();

            _context.AppItems.Remove(app);
            await _context.SaveChangesAsync();
            await _live.AppsUpdatedAsync();
            return Ok();
        }
    }
}