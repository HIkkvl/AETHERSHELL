using System.IO.Compression;
using AetherShell.Server.Data;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Реестр клубов для личного кабинета.
    /// PlatformAdmin видит и создаёт все клубы, Owner видит только свои.
    /// </summary>
    [ApiController]
    [Route("api/clubs")]
    [Authorize]
    public class ClubsController : ControllerBase
    {
        private readonly PlatformDbContext _db;
        private readonly ICurrentClub _currentClub;
        private readonly ServerSettings _settings;
        private readonly ClubProvisioningService _provisioning;
        private readonly IClubDbContextFactory _clubDb;

        public ClubsController(
            PlatformDbContext db,
            ICurrentClub currentClub,
            ServerSettings settings,
            ClubProvisioningService provisioning,
            IClubDbContextFactory clubDb)
        {
            _db = db;
            _currentClub = currentClub;
            _settings = settings;
            _provisioning = provisioning;
            _clubDb = clubDb;
        }

        private bool IsPlatformAdmin => _currentClub.IsPlatformAdmin;
        private int? AccountId => _currentClub.AccountId;

        [HttpGet]
        public async Task<IActionResult> GetClubs(CancellationToken ct)
        {
            if (AccountId == null) return Unauthorized();

            var query = _db.Clubs.AsNoTracking();
            if (!IsPlatformAdmin)
                query = query.Where(c => c.OwnerId == AccountId);

            var clubs = await query
                .OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Slug,
                    c.City,
                    c.Address,
                    c.IsActive,
                    c.CreatedAt,
                    c.LastSeenAt,
                    ownerEmail = c.Owner.Email
                })
                .ToListAsync(ct);

            // Счётчики ПК живут в базе каждого клуба, поэтому одним запросом их
            // больше не собрать: обходим клубы по очереди.
            var result = new List<object>(clubs.Count);
            foreach (var club in clubs)
            {
                var (total, online, pending) = await CountComputersAsync(club.Id, ct);
                result.Add(new
                {
                    club.Id,
                    club.Name,
                    club.Slug,
                    club.City,
                    club.Address,
                    club.IsActive,
                    club.CreatedAt,
                    club.LastSeenAt,
                    club.ownerEmail,
                    computersTotal = total,
                    computersOnline = online,
                    computersPending = pending
                });
            }

            return Ok(result);
        }

        /// <summary>
        /// Публичное разрешение slug → id для адреса /panel/{slug}.
        /// Отдаёт только id/name/slug — без ключей и настроек.
        /// </summary>
        [HttpGet("resolve/{slug}")]
        [AllowAnonymous]
        public async Task<IActionResult> ResolveBySlug(string slug, CancellationToken ct)
        {
            slug = (slug ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(slug) || slug is "assets" or "images" or "klub")
                return NotFound(new { error = "Клуб не найден" });

            var club = await _db.Clubs.AsNoTracking()
                .Where(c => c.Slug == slug && c.IsActive)
                .Select(c => new { c.Id, c.Name, c.Slug })
                .FirstOrDefaultAsync(ct);

            return club == null
                ? NotFound(new { error = "Клуб не найден" })
                : Ok(club);
        }

        /// <summary>Старые закладки /panel/klub/1 → новый slug.</summary>
        [HttpGet("resolve-id/{id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> ResolveById(int id, CancellationToken ct)
        {
            var club = await _db.Clubs.AsNoTracking()
                .Where(c => c.Id == id && c.IsActive)
                .Select(c => new { c.Id, c.Name, c.Slug })
                .FirstOrDefaultAsync(ct);

            return club == null
                ? NotFound(new { error = "Клуб не найден" })
                : Ok(club);
        }

        /// <summary>
        /// Счётчики ПК клуба. База клуба может быть недоступна (например, ещё
        /// разворачивается) — тогда показываем нули вместо ошибки на весь список.
        /// </summary>
        private async Task<(int Total, int Online, int Pending)> CountComputersAsync(int clubId, CancellationToken ct)
        {
            try
            {
                await using var db = _clubDb.Create(clubId);
                var total = await db.Computers.CountAsync(x => x.IsApproved, ct);
                var online = await db.Computers.CountAsync(x => x.IsOnline, ct);
                var pending = await db.Computers.CountAsync(x => !x.IsApproved, ct);
                return (total, online, pending);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Clubs] Не удалось прочитать базу клуба {clubId}: {ex.Message}");
                return (0, 0, 0);
            }
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetClub(int id, CancellationToken ct)
        {
            var club = await FindAccessibleClubAsync(id, ct);
            if (club == null) return NotFound(new { error = "Клуб не найден" });

            return Ok(new
            {
                club.Id,
                club.Name,
                club.Slug,
                club.City,
                club.Address,
                club.IsActive,
                club.CreatedAt,
                club.LastSeenAt,
                club.EnrollmentKey,
                club.RequireComputerApproval,
                club.EnableShop,
                club.LoyaltyFirstThreshold,
                club.LoyaltyStep,
                club.MaxDiscountPercent
            });
        }

        /// <summary>
        /// Создаёт клуб вместе с аккаунтом владельца. Это та самая «одна кнопка»:
        /// на выходе — email и пароль, которые можно отдать клиенту, и ключ для установщика.
        /// Пароль возвращается один раз и больше нигде не хранится в открытом виде.
        /// </summary>
        [HttpPost]
        [Authorize(Roles = AccountRoles.PlatformAdmin)]
        public async Task<IActionResult> CreateClub([FromBody] CreateClubRequest request, CancellationToken ct)
        {
            try
            {
                var result = await _provisioning.CreateAsync(new ClubProvisionRequest
                {
                    Name = request.Name,
                    OwnerEmail = request.OwnerEmail,
                    OwnerName = request.OwnerName,
                    OwnerPhone = request.OwnerPhone,
                    City = request.City,
                    Address = request.Address,
                    LeadId = request.LeadId
                }, ct);

                return Ok(new
                {
                    clubId = result.ClubId,
                    clubName = result.ClubName,
                    slug = result.Slug,
                    enrollmentKey = result.EnrollmentKey,
                    ownerEmail = result.OwnerEmail,
                    // null, если владелец уже существовал: у него свой пароль
                    password = result.Password,
                    emailSent = result.EmailSent
                });
            }
            catch (ClubProvisioningException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateClub(int id, [FromBody] UpdateClubRequest request, CancellationToken ct)
        {
            var club = await FindAccessibleClubAsync(id, ct);
            if (club == null) return NotFound(new { error = "Клуб не найден" });

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var newName = request.Name.Trim();
                if (!string.Equals(club.Name, newName, StringComparison.Ordinal))
                {
                    club.Name = newName;
                    var taken = await _db.Clubs.Where(c => c.Id != club.Id).Select(c => c.Slug).ToListAsync(ct);
                    club.Slug = ClubSlug.EnsureUnique(ClubSlug.FromName(newName), taken);
                }
            }
            if (request.City != null) club.City = request.City;
            if (request.Address != null) club.Address = request.Address;
            if (request.RequireComputerApproval is bool requireApproval) club.RequireComputerApproval = requireApproval;
            if (request.EnableShop is bool enableShop) club.EnableShop = enableShop;
            if (request.LoyaltyFirstThreshold is decimal first) club.LoyaltyFirstThreshold = first;
            if (request.LoyaltyStep is decimal step) club.LoyaltyStep = step;
            if (request.MaxDiscountPercent is int maxDiscount) club.MaxDiscountPercent = maxDiscount;

            // Отключать и включать клуб может только платформа.
            if (request.IsActive is bool isActive && IsPlatformAdmin) club.IsActive = isActive;

            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Сохранено", slug = club.Slug });
        }

        /// <summary>
        /// Полное удаление клуба и всех его данных: база клуба сносится целиком.
        /// Только владелец платформы. Аккаунт владельца клуба удаляется, если у него
        /// больше нет других клубов.
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Roles = AccountRoles.PlatformAdmin)]
        public async Task<IActionResult> DeleteClub(int id, CancellationToken ct)
        {
            var club = await _db.Clubs.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (club == null) return NotFound(new { error = "Клуб не найден" });

            var clubName = club.Name;
            await _provisioning.DeleteAsync(club, ct);

            return Ok(new { message = $"Клуб «{clubName}» удалён" });
        }

        /// <summary>
        /// Перевыпуск ключа: старые установки шелла перестают подключаться,
        /// поэтому в клубе нужно переустановить конфиг.
        /// </summary>
        [HttpPost("{id:int}/rotate-key")]
        public async Task<IActionResult> RotateEnrollmentKey(int id, CancellationToken ct)
        {
            var club = await FindAccessibleClubAsync(id, ct);
            if (club == null) return NotFound(new { error = "Клуб не найден" });

            club.EnrollmentKey = PasswordHasher.GenerateEnrollmentKey();
            await _db.SaveChangesAsync(ct);

            return Ok(new { enrollmentKey = club.EnrollmentKey });
        }

        /// <summary>
        /// Установщик, подшитый под клуб: exe плюс server.config с адресом сервера
        /// и ключом клуба. Клиенту не нужно ничего вводить — ни IP, ни порт.
        /// </summary>
        [HttpGet("{id:int}/installer")]
        public async Task<IActionResult> DownloadInstaller(int id, CancellationToken ct)
        {
            var club = await FindAccessibleClubAsync(id, ct);
            if (club == null) return NotFound(new { error = "Клуб не найден" });

            var installerExe = ResolveInstallerPath();
            if (installerExe == null)
                return NotFound(new { error = "Установщик не залит на сервер: положите SpikInstaller.exe рядом с сервером" });

            var config = string.Join("\n",
                $"SERVER_URL={PublicUrl()}",
                $"CLUB_KEY={club.EnrollmentKey}",
                $"CLUB_NAME={club.Name}");

            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                archive.CreateEntryFromFile(installerExe, "SpikInstaller.exe", CompressionLevel.Optimal);

                var entry = archive.CreateEntry("server.config", CompressionLevel.Optimal);
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync(config);
            }
            zip.Position = 0;

            var fileName = $"AetherShell-{ClubSlug.FromName(club.Name)}.zip";
            return File(zip, "application/zip", fileName);
        }

        /// <summary>
        /// Адрес, по которому шелл будет подключаться. За реверс-прокси берётся
        /// из настроек, иначе — origin текущего запроса.
        /// </summary>
        private string PublicUrl()
        {
            var configured = Environment.GetEnvironmentVariable("PUBLIC_URL") ?? _settings.PublicUrl;
            return string.IsNullOrWhiteSpace(configured)
                ? $"{Request.Scheme}://{Request.Host}"
                : configured.TrimEnd('/');
        }

        private static string? ResolveInstallerPath()
        {
            var baseDir = AppContext.BaseDirectory;
            string[] candidates =
            [
                Path.Combine(baseDir, "SpikInstaller.exe"),
                Path.Combine(baseDir, "AetherShell.Installer.exe"),
                Path.Combine(baseDir, "Installer", "SpikInstaller.exe"),
                Path.Combine(baseDir, "Installer", "AetherShell.Installer.exe")
            ];
            return candidates.FirstOrDefault(System.IO.File.Exists);
        }

        /// <summary>
        /// Первый администратор зала: аккаунт для входа в /panel.
        /// Владелец создаёт его сам, отдельно от своего платформенного аккаунта.
        /// </summary>
        [HttpPost("{id:int}/staff")]
        public async Task<IActionResult> CreateStaff(int id, [FromBody] CreateStaffRequest request, CancellationToken ct)
        {
            var club = await FindAccessibleClubAsync(id, ct);
            if (club == null) return NotFound(new { error = "Клуб не найден" });

            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Trim().Length < 4)
                return BadRequest(new { error = "Логин должен быть не короче 4 символов" });

            var role = string.IsNullOrWhiteSpace(request.Role) ? "Super" : request.Role.Trim();
            if (role is not ("Admin" or "Senior" or "Super"))
                return BadRequest(new { error = "Роль должна быть Admin, Senior или Super" });

            var username = request.Username.Trim();

            await using var clubDb = _clubDb.Create(club.Id);

            var exists = await clubDb.Users.AnyAsync(u => u.Username == username, ct);
            if (exists) return Conflict(new { error = "Такой логин в этом клубе уже есть" });

            var password = string.IsNullOrWhiteSpace(request.Password)
                ? PasswordHasher.GenerateReadablePassword(10)
                : request.Password;

            clubDb.Users.Add(new User
            {
                Username = username,
                PasswordHash = PasswordHasher.Hash(password),
                Email = request.Email ?? "",
                Role = role,
                CreatedAt = DateTime.UtcNow
            });
            await clubDb.SaveChangesAsync(ct);

            return Ok(new
            {
                username,
                role,
                // Показывается один раз, если пароль сгенерирован сервером.
                password = string.IsNullOrWhiteSpace(request.Password) ? password : null
            });
        }

        /// <summary>Клуб, к которому у текущего аккаунта есть доступ, иначе null.</summary>
        private async Task<Club?> FindAccessibleClubAsync(int id, CancellationToken ct)
        {
            if (AccountId == null) return null;

            return await _db.Clubs.FirstOrDefaultAsync(
                c => c.Id == id && (IsPlatformAdmin || c.OwnerId == AccountId), ct);
        }
    }

    public class CreateClubRequest
    {
        public string Name { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string? OwnerName { get; set; }
        public string? OwnerPhone { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }

        /// <summary>Заявка с лендинга, по которой создаётся клуб.</summary>
        public int? LeadId { get; set; }
    }

    public class UpdateClubRequest
    {
        public string? Name { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }
        public bool? IsActive { get; set; }
        public bool? RequireComputerApproval { get; set; }
        public bool? EnableShop { get; set; }
        public decimal? LoyaltyFirstThreshold { get; set; }
        public decimal? LoyaltyStep { get; set; }
        public int? MaxDiscountPercent { get; set; }
    }

    public class CreateStaffRequest
    {
        public string Username { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
    }
}
