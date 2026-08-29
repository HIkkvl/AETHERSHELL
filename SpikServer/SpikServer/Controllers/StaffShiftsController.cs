using System.Security.Claims;
using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Смены персонала: старт при входе в панель, при повторном входе —
    /// подтверждение окончания прошлой смены с кратким отчётом.
    /// </summary>
    [ApiController]
    [Route("api/StaffShifts")]
    [Authorize(Roles = "Admin,Senior,Super")]
    [RequireClub]
    public class StaffShiftsController : ControllerBase
    {
        private static readonly HashSet<string> SummarySkipTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Shift"
        };

        private static readonly Dictionary<string, string> ActionLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Money"] = "Деньги / баланс",
            ["Session"] = "Сессии ПК",
            ["Shop"] = "Заказы / меню",
            ["Stock"] = "Склад",
            ["UserMgmt"] = "Клиенты / сотрудники",
            ["Settings"] = "Настройки",
            ["Map"] = "Карта клуба",
            ["Power"] = "Питание ПК",
            ["Broadcast"] = "Рассылки",
            ["Banners"] = "Баннеры",
            ["SaveTime"] = "Сохранение времени",
            ["Refund"] = "Возвраты",
        };

        private readonly ClubDbContext _db;
        private readonly AuditLogger _logger;

        public StaffShiftsController(ClubDbContext db, AuditLogger logger)
        {
            _db = db;
            _logger = logger;
        }

        private int? StaffUserId =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private string StaffUsername => User.Identity?.Name ?? "";

        [HttpGet("mine")]
        public async Task<IActionResult> Mine([FromQuery] int take = 20)
        {
            if (StaffUserId is not int userId)
                return Unauthorized();

            if (take < 1) take = 1;
            if (take > 100) take = 100;

            var open = await _db.StaffShifts
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            var recent = await _db.StaffShifts
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.StartedAt)
                .Take(take)
                .Select(s => ToDto(s))
                .ToListAsync();

            return Ok(new
            {
                current = open == null ? null : ToDto(open),
                recent
            });
        }

        /// <summary>
        /// После входа в панель: продолжить открытую смену или открыть новую.
        /// Отчёт и подтверждение окончания — при выходе из панели.
        /// </summary>
        [HttpPost("enter")]
        public async Task<IActionResult> Enter([FromQuery] int? knownShiftId = null)
        {
            if (StaffUserId is not int userId)
                return Unauthorized();

            var open = await _db.StaffShifts
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (open != null)
            {
                return Ok(new
                {
                    status = "active",
                    shift = ToDto(open)
                });
            }

            var shift = new StaffShift
            {
                UserId = userId,
                Username = StaffUsername,
                StartedAt = DateTime.UtcNow
            };
            _db.StaffShifts.Add(shift);
            await _db.SaveChangesAsync();
            await _logger.LogAsync(StaffUsername, "Shift", StaffUsername, "Начало смены (вход в панель)");

            return Ok(new
            {
                status = "started",
                shift = ToDto(shift)
            });
        }

        /// <summary>Отчёт по текущей открытой смене (для окна при выходе).</summary>
        [HttpGet("summary")]
        public async Task<IActionResult> CurrentSummary()
        {
            if (StaffUserId is not int userId)
                return Unauthorized();

            var open = await _db.StaffShifts
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (open == null)
                return Ok(new { hasOpen = false });

            return Ok(new
            {
                hasOpen = true,
                shift = ToDto(open),
                summary = await BuildSummaryAsync(open)
            });
        }

        /// <summary>Подтвердить конец прошлой смены и сразу открыть новую.</summary>
        [HttpPost("confirm-reauth")]
        public async Task<IActionResult> ConfirmReauth()
        {
            if (StaffUserId is not int userId)
                return Unauthorized();

            var open = await _db.StaffShifts
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (open == null)
            {
                var fresh = new StaffShift
                {
                    UserId = userId,
                    Username = StaffUsername,
                    StartedAt = DateTime.UtcNow
                };
                _db.StaffShifts.Add(fresh);
                await _db.SaveChangesAsync();
                await _logger.LogAsync(StaffUsername, "Shift", StaffUsername, "Начало смены (вход в панель)");
                return Ok(new { status = "started", closed = false, shift = ToDto(fresh) });
            }

            var summary = await BuildSummaryAsync(open);
            open.EndedAt = DateTime.UtcNow;
            open.EndReason = StaffShiftEndReason.Reauth;
            await _db.SaveChangesAsync();

            await _logger.LogAsync(StaffUsername, "Shift", StaffUsername,
                $"Конец смены (повторный вход), длительность {FormatDuration(open.StartedAt, open.EndedAt.Value)}");

            var next = new StaffShift
            {
                UserId = userId,
                Username = StaffUsername,
                StartedAt = DateTime.UtcNow
            };
            _db.StaffShifts.Add(next);
            await _db.SaveChangesAsync();
            await _logger.LogAsync(StaffUsername, "Shift", StaffUsername, "Начало смены (вход в панель)");

            return Ok(new
            {
                status = "rotated",
                closed = true,
                previous = ToDto(open),
                summary,
                shift = ToDto(next)
            });
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            if (StaffUserId is not int userId)
                return Unauthorized();

            var open = await _db.StaffShifts
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (open != null)
            {
                return Ok(new
                {
                    message = "Смена уже начата",
                    alreadyOpen = true,
                    shift = ToDto(open)
                });
            }

            var shift = new StaffShift
            {
                UserId = userId,
                Username = StaffUsername,
                StartedAt = DateTime.UtcNow
            };
            _db.StaffShifts.Add(shift);
            await _db.SaveChangesAsync();

            await _logger.LogAsync(StaffUsername, "Shift", StaffUsername, "Начало смены");
            return Ok(new { message = "Смена начата", alreadyOpen = false, shift = ToDto(shift) });
        }

        [HttpPost("end")]
        public async Task<IActionResult> End([FromQuery] string? reason = null)
        {
            if (StaffUserId is not int userId)
                return Unauthorized();

            var endReason = ParseReason(reason) ?? StaffShiftEndReason.Manual;
            var open = await _db.StaffShifts
                .Where(s => s.UserId == userId && s.EndedAt == null)
                .OrderByDescending(s => s.StartedAt)
                .FirstOrDefaultAsync();

            if (open == null)
                return Ok(new { message = "Открытой смены нет", closed = false });

            open.EndedAt = DateTime.UtcNow;
            open.EndReason = endReason;
            await _db.SaveChangesAsync();

            await _logger.LogAsync(StaffUsername, "Shift", StaffUsername,
                $"Конец смены ({endReason}), длительность {FormatDuration(open.StartedAt, open.EndedAt.Value)}");

            return Ok(new { message = "Смена завершена", closed = true, shift = ToDto(open) });
        }

        [HttpGet]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> List([FromQuery] int take = 50, [FromQuery] string? username = null)
        {
            if (take < 1) take = 1;
            if (take > 200) take = 200;

            var query = _db.StaffShifts.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(username))
                query = query.Where(s => s.Username == username);

            var rows = await query
                .OrderByDescending(s => s.StartedAt)
                .Take(take)
                .Select(s => ToDto(s))
                .ToListAsync();

            return Ok(rows);
        }

        private async Task<object> BuildSummaryAsync(StaffShift shift)
        {
            var from = shift.StartedAt;
            var to = shift.EndedAt ?? DateTime.UtcNow;
            var username = shift.Username;

            var logs = await _db.AdminLogs
                .AsNoTracking()
                .Where(l => l.AdminName == username
                            && l.CreatedAt >= from
                            && l.CreatedAt <= to
                            && !SummarySkipTypes.Contains(l.ActionType))
                .OrderByDescending(l => l.CreatedAt)
                .Take(200)
                .ToListAsync();

            var byType = logs
                .GroupBy(l => l.ActionType)
                .Select(g => new
                {
                    type = g.Key,
                    label = ActionLabels.TryGetValue(g.Key, out var label) ? label : g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .ToList();

            var recent = logs.Take(15).Select(l => new
            {
                l.Id,
                l.ActionType,
                label = ActionLabels.TryGetValue(l.ActionType, out var label) ? label : l.ActionType,
                l.Target,
                l.Details,
                l.CreatedAt
            }).ToList();

            return new
            {
                startedAt = shift.StartedAt,
                endedAt = to,
                durationMinutes = (int)Math.Max(0, (to - from).TotalMinutes),
                totalActions = logs.Count,
                byType,
                recent
            };
        }

        private static StaffShiftEndReason? ParseReason(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return null;
            return Enum.TryParse<StaffShiftEndReason>(reason, true, out var parsed) ? parsed : null;
        }

        private static object ToDto(StaffShift s) => new
        {
            s.Id,
            s.UserId,
            s.Username,
            s.StartedAt,
            s.EndedAt,
            endReason = s.EndReason.HasValue ? s.EndReason.Value.ToString() : null,
            durationMinutes = s.EndedAt.HasValue
                ? (int)Math.Max(0, (s.EndedAt.Value - s.StartedAt).TotalMinutes)
                : (int)Math.Max(0, (DateTime.UtcNow - s.StartedAt).TotalMinutes),
            isOpen = s.EndedAt == null
        };

        private static string FormatDuration(DateTime start, DateTime end)
        {
            var span = end - start;
            if (span.TotalHours >= 1)
                return $"{(int)span.TotalHours} ч {span.Minutes} мин";
            return $"{Math.Max(0, (int)span.TotalMinutes)} мин";
        }
    }
}
