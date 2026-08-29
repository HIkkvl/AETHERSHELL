using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AetherShell.Server.Controllers
{
    /// <summary>Профиль посетителя в шелле: аватар и смена пароля.</summary>
    [Route("api/Auth")]
    [ApiController]
    [Authorize(Roles = "User")]
    [RequireClub]
    public class ClientProfileController : ControllerBase
    {
        private const long MaxAvatarBytes = 3 * 1024 * 1024;
        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        private readonly PlatformDbContext _platform;
        private readonly UploadStorage _storage;
        private readonly ICurrentClub _currentClub;
        private readonly ILogger<ClientProfileController> _log;

        public ClientProfileController(
            PlatformDbContext platform,
            UploadStorage storage,
            ICurrentClub currentClub,
            ILogger<ClientProfileController> log)
        {
            _platform = platform;
            _storage = storage;
            _currentClub = currentClub;
            _log = log;
        }

        private int? ClientId =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private IQueryable<Client> NetworkClients =>
            _platform.Clients.Where(c => c.NetworkId == _currentClub.NetworkId);

        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (ClientId is not int id) return Unauthorized();
            if (_currentClub.NetworkId == null) return StatusCode(503, "Клуб недоступен");

            var client = await NetworkClients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();

            return Ok(new
            {
                client.Id,
                client.Username,
                client.Email,
                client.Balance,
                client.AvatarUrl,
                client.TotalSpent
            });
        }

        public class ClientChangePasswordDto
        {
            public string? CurrentPassword { get; set; }
            public string NewPassword { get; set; } = "";
        }

        [HttpPost("client-change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ClientChangePasswordDto request)
        {
            if (ClientId is not int id) return Unauthorized();
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
                return BadRequest("Новый пароль должен быть не короче 6 символов");

            var client = await NetworkClients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();

            if (!PasswordHasher.Verify(request.CurrentPassword ?? "", client.PasswordHash))
                return BadRequest("Текущий пароль указан неверно");

            client.PasswordHash = PasswordHasher.Hash(request.NewPassword);
            await _platform.SaveChangesAsync();
            return Ok(new { message = "Пароль изменён" });
        }

        [HttpPost("avatar")]
        [RequestSizeLimit(MaxAvatarBytes + 1024)]
        public async Task<IActionResult> UploadAvatar(IFormFile file, CancellationToken ct)
        {
            if (ClientId is not int id) return Unauthorized();
            if (_currentClub.ClubId == null) return StatusCode(503, "Клуб недоступен");
            if (file == null || file.Length == 0) return BadRequest("Файл не выбран");
            if (file.Length > MaxAvatarBytes) return BadRequest("Файл больше 3 МБ");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                return BadRequest("Разрешены JPG, PNG и WEBP");

            var client = await NetworkClients.FirstOrDefaultAsync(c => c.Id == id);
            if (client == null) return NotFound();

            var clubId = _currentClub.ClubId.Value;
            var fileName = $"avatar-{client.Id}-{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(_storage.ClubPath(clubId), fileName);

            try
            {
                await using var stream = System.IO.File.Create(fullPath);
                await file.CopyToAsync(stream, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Avatar] Не удалось сохранить {File}", fullPath);
                return StatusCode(500, "Не удалось сохранить файл");
            }

            var url = $"{UploadStorage.PublicPrefix}/club-{clubId}/{fileName}";
            client.AvatarUrl = url;
            await _platform.SaveChangesAsync();

            return Ok(new { url = client.AvatarUrl });
        }
    }
}
