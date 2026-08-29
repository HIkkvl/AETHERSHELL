using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.DTOs;
using AetherShell.Server.Services;
using AetherShell.Server.Models;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Персонал зала. Живёт в базе своего клуба, поэтому доступ сотрудника не
    /// распространяется на другие филиалы сети.
    ///
    /// Посетители обслуживаются отдельно (<see cref="ClientsController"/>): у них
    /// баланс общий на всю сеть, и лежат они в платформенной базе.
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    [AetherShell.Server.Filters.RequireClub]
    public class UsersController : ControllerBase
    {
        /// <summary>Роли, которые можно выдать сотруднику зала.</summary>
        private static readonly string[] StaffRoles = { "Admin", "Senior", "Super" };

        private readonly ClubDbContext _context;
        private readonly PlatformDbContext _platform;
        private readonly AuditLogger _logger;
        private readonly ICurrentClub _currentClub;

        public UsersController(
            ClubDbContext context,
            PlatformDbContext platform,
            AuditLogger logger,
            ICurrentClub currentClub)
        {
            _context = context;
            _platform = platform;
            _logger = logger;
            _currentClub = currentClub;
        }

        [Authorize(Roles = "Super")]
        [HttpGet]
        public async Task<IActionResult> GetUsers([FromQuery] string? search = null)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(u => u.Username.Contains(search));
            }

            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.Email,
                    u.Role,
                    u.CreatedAt,
                    Balance = 0m,

                    // Сотрудник за ПК не сидит, но панель ждёт эти поля в общей таблице.
                    CurrentPcName = (string?)null,
                    CurrentPcDisplay = (string?)null
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("create")]
        [Authorize(Roles = "Super")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Заполните логин и пароль");

            if (!StaffRoles.Contains(request.Role))
                return BadRequest("Недопустимая роль. Клиенты создаются через /api/clients.");

            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest("Пользователь уже существует");

            // Логин, занятый посетителем сети, брать нельзя: при входе в зале
            // сотрудник проверяется первым и перекрыл бы клиента.
            var networkId = _currentClub.NetworkId;
            if (networkId != null
                && await _platform.Clients.AnyAsync(c => c.NetworkId == networkId && c.Username == request.Username))
                return BadRequest("Этот логин занят клиентом сети");

            var newUser = new User
            {
                Username = request.Username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                Email = request.Email ?? "",
                Role = request.Role,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            await _logger.LogAsync("SuperAdmin", "UserMgmt", request.Username, $"Создан сотрудник: {request.Role}");

            return Ok(new { message = "Пользователь создан" });
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Super")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (user.Username == User.Identity?.Name)
                return BadRequest("Нельзя удалить себя");

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            await _logger.LogAsync(User.Identity?.Name ?? "SuperAdmin", "UserMgmt", user.Username, "Сотрудник удалён");

            return Ok(new { message = "Удалено" });
        }
    }
}
