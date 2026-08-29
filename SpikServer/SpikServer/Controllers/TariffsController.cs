using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;

namespace AetherShell.Server.Controllers
{
    // Убрали [Authorize] с уровня класса, чтобы не блокировать доступ по умолчанию
    [Route("api/[controller]")]
    [ApiController]
    [AetherShell.Server.Filters.RequireClub]
    public class TariffsController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly ClubRealtimeNotifier _live;

        public TariffsController(ClubDbContext context, ClubRealtimeNotifier live)
        {
            _context = context;
            _live = live;
        }

        // GET: api/Tariffs
        [HttpGet]
        [AllowAnonymous] // Оставили для явности, теперь доступ открыт всем
        public async Task<IActionResult> GetTariffs()
        {
            var tariffs = await _context.Tariffs
                .Where(t => t.IsActive)
                .OrderBy(t => t.Price)
                .ToListAsync();
            return Ok(tariffs);
        }

        [HttpPost]
        [Authorize(Roles = "Senior,Super")] // Перенесли защиту сюда
        public async Task<IActionResult> CreateTariff([FromBody] Tariff tariff)
        {
            if (string.IsNullOrWhiteSpace(tariff.Name))
                return BadRequest("Название обязательно");

            // ИЗМЕНЕНО: Проверка длительности
            // Если тариф НЕ пакетный (обычный), то длительность должна быть > 0.
            // Если пакетный (IsFixedTime = true), то длительность может быть 0.
            if (!tariff.IsFixedTime && tariff.DurationMinutes <= 0)
                return BadRequest("Длительность должна быть > 0 для почасовых тарифов");

            // Доп. валидация для пакетных тарифов
            if (tariff.IsFixedTime && !tariff.EndHour.HasValue)
                return BadRequest("Для пакетного тарифа нужно указать час окончания");

            _context.Tariffs.Add(tariff);
            await _context.SaveChangesAsync();
            await _live.TariffsUpdatedAsync();
            return Ok(tariff);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Senior,Super")] // Перенесли защиту сюда
        public async Task<IActionResult> DeleteTariff(int id)
        {
            var tariff = await _context.Tariffs.FindAsync(id);
            if (tariff == null) return NotFound();

            tariff.IsActive = false;
            await _context.SaveChangesAsync();
            await _live.TariffsUpdatedAsync();

            return Ok();
        }

        [HttpPut("{id}")]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> UpdateTariff(int id, [FromBody] Tariff updatedTariff)
        {
            var tariff = await _context.Tariffs.FindAsync(id);
            if (tariff == null) return NotFound();

            tariff.Name = updatedTariff.Name;
            tariff.Price = updatedTariff.Price;
            tariff.DurationMinutes = updatedTariff.DurationMinutes;
            tariff.StartHour = updatedTariff.StartHour;
            tariff.EndHour = updatedTariff.EndHour;
            tariff.IsFixedTime = updatedTariff.IsFixedTime;
            tariff.IsBurnable = updatedTariff.IsBurnable;
            tariff.Feature1 = updatedTariff.Feature1;
            tariff.Feature2 = updatedTariff.Feature2;

            await _context.SaveChangesAsync();
            await _live.TariffsUpdatedAsync();
            return Ok(tariff);
        }
    }
}