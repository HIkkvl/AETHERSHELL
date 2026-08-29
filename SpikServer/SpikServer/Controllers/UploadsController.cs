using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Загрузка картинок для игр, товаров и баннеров. Раньше в панели можно было
    /// только вставить ссылку, из-за чего обложки жили на сторонних хостингах.
    /// </summary>
    [Route("api/uploads")]
    [ApiController]
    [Authorize(Roles = "Senior,Super")]
    [RequireClub]
    public class UploadsController : ControllerBase
    {
        private const long MaxBytes = 5 * 1024 * 1024;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        private static readonly Dictionary<string, string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif"
        };

        private readonly UploadStorage _storage;
        private readonly ICurrentClub _currentClub;
        private readonly ILogger<UploadsController> _log;

        public UploadsController(UploadStorage storage, ICurrentClub currentClub, ILogger<UploadsController> log)
        {
            _storage = storage;
            _currentClub = currentClub;
            _log = log;
        }

        [HttpPost("image")]
        [RequestSizeLimit(MaxBytes + 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Файл не выбран" });

            if (file.Length > MaxBytes)
                return BadRequest(new { error = "Файл больше 5 МБ" });

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                return BadRequest(new { error = "Разрешены только JPG, PNG, WEBP и GIF" });

            if (!AllowedContentTypes.TryGetValue(extension, out var expectedContentType)
                || !file.ContentType.Equals(expectedContentType, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Тип файла не совпадает с расширением" });
            }

            var clubId = _currentClub.ClubId!.Value;
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(_storage.ClubPath(clubId), fileName);

            try
            {
                await using var stream = System.IO.File.Create(fullPath);
                await file.CopyToAsync(stream, ct);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Uploads] Не удалось сохранить {File}", fullPath);
                return StatusCode(500, new { error = "Не удалось сохранить файл" });
            }

            var url = $"{UploadStorage.PublicPrefix}/club-{clubId}/{fileName}";
            return Ok(new { url });
        }
    }
}
