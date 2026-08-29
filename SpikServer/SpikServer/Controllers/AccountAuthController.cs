using AetherShell.Server.Data;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Вход в личный кабинет по данным, которые выдаются клиенту при подключении клуба.
    /// Здесь же живёт смена пароля — сгенерированный пароль нужно поменять при первом входе.
    /// </summary>
    [ApiController]
    [Route("api/account")]
    public class AccountAuthController : ControllerBase
    {
        private readonly PlatformDbContext _db;
        private readonly TokenService _tokens;
        private readonly ICurrentClub _currentClub;

        public AccountAuthController(PlatformDbContext db, TokenService tokens, ICurrentClub currentClub)
        {
            _db = db;
            _tokens = tokens;
            _currentClub = currentClub;
        }

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Login([FromBody] AccountLoginRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Email и пароль обязательны" });

            var email = request.Email.Trim().ToLowerInvariant();
            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Email == email, ct);

            if (account == null || !PasswordHasher.Verify(request.Password, account.PasswordHash))
                return Unauthorized(new { error = "Неверный email или пароль" });

            if (!account.IsActive)
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "Аккаунт отключён" });

            account.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                token = _tokens.IssueAccountToken(account),
                accountId = account.Id,
                email = account.Email,
                displayName = account.DisplayName,
                role = account.Role,
                mustChangePassword = account.MustChangePassword
            });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            var account = await CurrentAccountAsync(ct);
            if (account == null) return Unauthorized();

            var clubsCount = await _db.Clubs.CountAsync(
                c => account.Role == AccountRoles.PlatformAdmin || c.OwnerId == account.Id, ct);

            return Ok(new
            {
                id = account.Id,
                email = account.Email,
                displayName = account.DisplayName,
                role = account.Role,
                mustChangePassword = account.MustChangePassword,
                clubsCount
            });
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
                return BadRequest(new { error = "Новый пароль должен быть не короче 8 символов" });

            var account = await CurrentAccountAsync(ct);
            if (account == null) return Unauthorized();

            if (!PasswordHasher.Verify(request.CurrentPassword ?? "", account.PasswordHash))
                return BadRequest(new { error = "Текущий пароль указан неверно" });

            account.PasswordHash = PasswordHasher.Hash(request.NewPassword);
            account.MustChangePassword = false;
            await _db.SaveChangesAsync(ct);

            return Ok(new { message = "Пароль изменён" });
        }

        private Task<Account?> CurrentAccountAsync(CancellationToken ct)
        {
            var accountId = _currentClub.AccountId;
            if (accountId == null) return Task.FromResult<Account?>(null);
            return _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        }
    }

    public class AccountLoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }
        public string NewPassword { get; set; } = string.Empty;
    }
}
