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
    /// Заявки с лендинга. Обработать заявку может либо PlatformAdmin из кабинета,
    /// либо Telegram-бот кнопкой под сообщением — бот подтверждает себя заголовком
    /// <c>X-Bot-Secret</c>, потому что аккаунта в системе у него нет.
    /// </summary>
    [ApiController]
    [Route("api/leads")]
    public class LeadsController : ControllerBase
    {
        private readonly PlatformDbContext _db;
        private readonly ClubProvisioningService _provisioning;
        private readonly IConfiguration _configuration;
        private readonly ServerSettings _settings;
        private readonly ILogger<LeadsController> _log;

        public LeadsController(
            PlatformDbContext db,
            ClubProvisioningService provisioning,
            IConfiguration configuration,
            ServerSettings settings,
            ILogger<LeadsController> log)
        {
            _db = db;
            _provisioning = provisioning;
            _configuration = configuration;
            _settings = settings;
            _log = log;
        }

        [HttpPost]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> Submit([FromBody] SubmitLeadRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.ClubName))
                return BadRequest(new { error = "Укажите название клуба" });
            if (string.IsNullOrWhiteSpace(request.Phone) && string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Оставьте телефон или email для связи" });

            _db.Leads.Add(new Lead
            {
                ClubName = request.ClubName.Trim(),
                Phone = request.Phone?.Trim(),
                Email = request.Email?.Trim().ToLowerInvariant(),
                Comment = request.Comment?.Trim(),
                Status = LeadStatus.Pending,
                SubmittedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);

            return Ok(new { message = "Заявка отправлена, мы свяжемся с вами" });
        }

        [HttpGet]
        [Authorize(Roles = AccountRoles.PlatformAdmin)]
        public async Task<IActionResult> GetLeads([FromQuery] bool pendingOnly = true, CancellationToken ct = default)
        {
            var query = _db.Leads.AsNoTracking();
            if (pendingOnly) query = query.Where(l => l.Status == LeadStatus.Pending);

            var leads = await query
                .OrderByDescending(l => l.SubmittedAt)
                .Take(200)
                .Select(l => new
                {
                    l.Id,
                    l.ClubName,
                    l.Phone,
                    l.Email,
                    l.Comment,
                    l.SubmittedAt,
                    status = l.Status.ToString(),
                    l.CreatedClubId,
                    l.TelegramMessageId
                })
                .ToListAsync(ct);

            return Ok(leads);
        }

        /// <summary>
        /// Принять заявку: создаёт клуб с отдельной базой, аккаунт владельца и
        /// отправляет ему письмо с доступом.
        /// </summary>
        [HttpPost("{id:int}/accept")]
        [AllowAnonymous]
        public async Task<IActionResult> Accept(int id, CancellationToken ct)
        {
            if (!IsTrustedCaller()) return Forbid();

            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lead == null) return NotFound(new { error = "Заявка не найдена" });

            if (lead.Status != LeadStatus.Pending)
                return BadRequest(new { error = $"Заявка уже обработана: {lead.Status}" });

            if (string.IsNullOrWhiteSpace(lead.Email))
                return BadRequest(new { error = "В заявке нет email — клуб придётся создать вручную в кабинете" });

            try
            {
                var result = await _provisioning.CreateAsync(new ClubProvisionRequest
                {
                    Name = lead.ClubName,
                    OwnerEmail = lead.Email!,
                    OwnerPhone = lead.Phone,
                    LeadId = lead.Id
                }, ct);

                return Ok(new
                {
                    message = "Клуб создан",
                    clubId = result.ClubId,
                    clubName = result.ClubName,
                    ownerEmail = result.OwnerEmail,
                    emailSent = result.EmailSent
                });
            }
            catch (ClubProvisioningException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[Leads] Не удалось создать клуб по заявке {LeadId}", id);
                return StatusCode(500, new { error = "Не удалось создать клуб, смотрите логи сервера" });
            }
        }

        [HttpPost("{id:int}/reject")]
        [AllowAnonymous]
        public async Task<IActionResult> Reject(int id, CancellationToken ct)
        {
            if (!IsTrustedCaller()) return Forbid();

            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lead == null) return NotFound();

            lead.Status = LeadStatus.Rejected;
            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Заявка отклонена" });
        }

        /// <summary>
        /// Бот запоминает, каким сообщением объявил заявку, чтобы после нажатия
        /// кнопки отредактировать именно его.
        /// </summary>
        [HttpPatch("{id:int}/message")]
        [AllowAnonymous]
        public async Task<IActionResult> SetMessage(int id, [FromBody] LeadMessageRequest request, CancellationToken ct)
        {
            if (!IsTrustedCaller()) return Forbid();

            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (lead == null) return NotFound();

            lead.TelegramMessageId = request.MessageId;
            await _db.SaveChangesAsync(ct);
            return Ok(new { message = "Сохранено" });
        }

        /// <summary>PlatformAdmin по токену либо бот по общему секрету.</summary>
        private bool IsTrustedCaller()
        {
            if (User.IsInRole(AccountRoles.PlatformAdmin)) return true;

            var expected =
                _configuration["TG_BOT_SECRET"]
                ?? Environment.GetEnvironmentVariable("TG_BOT_SECRET")
                ?? _settings.TelegramBotSecret;
            if (string.IsNullOrWhiteSpace(expected)) return false;

            var provided = Request.Headers["X-Bot-Secret"].ToString();
            if (string.IsNullOrEmpty(provided)) return false;

            var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
            var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
            if (expectedBytes.Length != providedBytes.Length) return false;

            // Сравнение фиксированного времени: секрет приходит извне.
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                providedBytes, expectedBytes);
        }
    }

    public class SubmitLeadRequest
    {
        public string ClubName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Comment { get; set; }
    }

    public class LeadMessageRequest
    {
        public long MessageId { get; set; }
    }
}
