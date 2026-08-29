using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Constants;
using AetherShell.Server.Data;
using AetherShell.Server.DTOs;
using AetherShell.Server.Filters;
using AetherShell.Server.Hubs;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AetherShell.Server.Controllers
{
    /// <summary>
    /// Вход в зале. Посетители живут в платформенной базе и принадлежат сети клубов
    /// (баланс общий на все филиалы), персонал — в базе своего клуба.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [RequireClub]
    public class AuthController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly PlatformDbContext _platform;
        private readonly IHubContext<ClubHub> _hubContext;
        private readonly SessionManager _sessionManager;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;
        private readonly ServerSettings _serverSettings;
        private readonly TokenService _tokens;
        private readonly ICurrentClub _currentClub;
        private readonly BalanceNotifier _balance;

        public AuthController(
            ClubDbContext context,
            PlatformDbContext platform,
            IHubContext<ClubHub> hubContext,
            SessionManager sessionManager,
            IConfiguration configuration,
            EmailService emailService,
            ServerSettings serverSettings,
            TokenService tokens,
            ICurrentClub currentClub,
            BalanceNotifier balance)
        {
            _context = context;
            _platform = platform;
            _hubContext = hubContext;
            _sessionManager = sessionManager;
            _configuration = configuration;
            _emailService = emailService;
            _serverSettings = serverSettings;
            _tokens = tokens;
            _currentClub = currentClub;
            _balance = balance;
        }

        /// <summary>Клуб текущего запроса. Гарантирован атрибутом <see cref="RequireClubAttribute"/>.</summary>
        private int ClubId => _currentClub.ClubId!.Value;

        /// <summary>Сеть клуба: посетители общие на все её филиалы.</summary>
        private int NetworkId => _currentClub.NetworkId!.Value;

        private IQueryable<Client> NetworkClients => _platform.Clients.Where(c => c.NetworkId == NetworkId);

        /// <summary>
        /// Клуб без сети означает, что запись клуба удалили между резолвом и запросом.
        /// Работать с посетителями в такой ситуации нельзя.
        /// </summary>
        private bool HasNetwork => _currentClub.NetworkId != null;

        // POST: api/Auth/register
        [HttpPost("register")]
        [EnableRateLimiting("login")] // Защита от спама регистрации
        public async Task<IActionResult> Register([FromBody] RegisterDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен, попробуйте позже.");

            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Логин и пароль обязательны.");

            // Валидация логина (минимум 4 символа)
            if (request.Username.Length < 4)
                return BadRequest("Логин должен содержать минимум 4 символа.");

            // Валидация пароля (минимум 6 символов)
            if (request.Password.Length < 6)
                return BadRequest("Пароль должен содержать минимум 6 символов.");

            // Валидация Email
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                if (!IsValidEmail(request.Email))
                    return BadRequest("Введите корректный Email адрес.");

                if (await NetworkClients.AnyAsync(c => c.Email == request.Email))
                    return BadRequest("Этот Email уже занят.");
            }

            if (await NetworkClients.AnyAsync(c => c.Username == request.Username))
                return BadRequest("Такой пользователь уже существует.");

            // Логин сотрудника этого зала занимать нельзя: иначе при входе было бы
            // непонятно, чей пароль проверять.
            if (await _context.Users.AnyAsync(u => u.Username == request.Username))
                return BadRequest("Такой пользователь уже существует.");

            string passwordHash = PasswordHasher.Hash(request.Password);

            var newClient = new Client
            {
                NetworkId = NetworkId,
                Username = request.Username,
                PasswordHash = passwordHash,
                Email = request.Email ?? "",
                Balance = 0,
                TotalSpent = 0,
                RemainingMinutes = 0,
                CreatedAt = DateTime.UtcNow,
                RegisteredClubId = ClubId
            };

            _platform.Clients.Add(newClient);
            await _platform.SaveChangesAsync();

            Console.WriteLine($"[Register] Новый посетитель создан: {newClient.Username} (ID: {newClient.Id}, сеть: {NetworkId})");

            return Ok(new { message = "Регистрация успешна! Теперь войдите." });
        }

        /// <summary>Профиль текущего сотрудника зала (club-токен).</summary>
        [HttpGet("me")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> Me()
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            var staff = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (staff == null) return NotFound(new { error = "Сотрудник не найден" });

            return Ok(new
            {
                id = staff.Id,
                username = staff.Username,
                email = staff.Email,
                role = string.IsNullOrEmpty(staff.Role) ? "Admin" : staff.Role,
                createdAt = staff.CreatedAt,
                kind = "staff"
            });
        }

        /// <summary>Смена пароля сотрудника зала.</summary>
        [HttpPost("change-password")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> ChangePassword([FromBody] StaffChangePasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
                return BadRequest(new { error = "Новый пароль должен быть не короче 6 символов" });

            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized();

            var staff = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (staff == null) return NotFound(new { error = "Сотрудник не найден" });

            if (!PasswordHasher.Verify(request.CurrentPassword ?? "", staff.PasswordHash))
                return BadRequest(new { error = "Текущий пароль указан неверно" });

            staff.PasswordHash = PasswordHasher.Hash(request.NewPassword);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Пароль изменён" });
        }

        // POST: api/Auth/login
        [HttpPost("login")]
        [EnableRateLimiting("login")] // Защита от брутфорса: 5 попыток в минуту
        public async Task<IActionResult> Login([FromBody] AetherShell.Server.DTOs.LoginRequest request)
        {
            // Сотрудник зала проверяется первым: этим же маршрутом шелл входит
            // в режим администратора.
            var staff = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (staff != null)
            {
                if (!PasswordHasher.Verify(request.Password, staff.PasswordHash))
                    return Unauthorized("Неверный логин или пароль");

                // Открытую смену не закрываем здесь: панель покажет отчёт и попросит подтверждение.

                return Ok(new
                {
                    token = _tokens.IssueClubToken(staff, ClubId),
                    role = string.IsNullOrEmpty(staff.Role) ? "Admin" : staff.Role,
                    username = staff.Username,
                    balance = 0m,
                    totalSpent = 0m,
                    hasActiveSession = false
                });
            }

            if (!HasNetwork) return StatusCode(503, "Клуб недоступен, попробуйте позже.");

            var client = await NetworkClients
                .Include(c => c.Group)
                .FirstOrDefaultAsync(c => c.Username == request.Username);

            if (client == null || !PasswordHasher.Verify(request.Password, client.PasswordHash))
                return Unauthorized("Неверный логин или пароль");

            var tokenString = _tokens.IssueClientToken(client, ClubId);
            var clubForLoyalty = await _platform.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ClubId);
            var discountPercent = Loyalty.EffectiveDiscount(client, clubForLoyalty);

            // Логика привязки к ПК
            if (!string.IsNullOrEmpty(request.MacAddress))
            {
                var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == request.MacAddress);
                if (pc != null)
                {
                    pc.CurrentUser = client.Username;
                    pc.IsOnline = true;
                    await _context.SaveChangesAsync();
                }

                var activeSession = await _sessionManager.GetActiveSessionAsync(ClubId, request.MacAddress);

                // 1. Сессия уже идет
                if (activeSession != null && activeSession.EndTime > DateTime.UtcNow)
                {
                    if (ClubHub.TryGetPcConnection(ClubId, request.MacAddress, out var connId) && connId != null)
                    {
                        await _hubContext.Clients.Client(connId).SendAsync(SignalRMethods.ReceiveUnlock, activeSession.EndTime);
                    }

                    return Ok(new
                    {
                        token = tokenString,
                        role = "User",
                        username = client.Username,
                        balance = client.Balance,
                        totalSpent = client.TotalSpent,
                        discountPercent,
                        avatarUrl = client.AvatarUrl,
                        hasActiveSession = true,
                        endTime = activeSession.EndTime
                    });
                }

                // 2. Восстановление времени
                if (client.RemainingMinutes > 0)
                {
                    int minutesToRestore = client.RemainingMinutes;
                    client.RemainingMinutes = 0;
                    var endTime = DateTime.UtcNow.AddMinutes(minutesToRestore);

                    if (pc != null)
                    {
                        pc.SessionEndTime = endTime;
                        pc.CurrentUser = client.Username;
                        pc.SessionSavesRemaining = true; // остаток с профиля — несгораемый
                        pc.IsOnline = true;
                    }
                    await _platform.SaveChangesAsync();
                    await _context.SaveChangesAsync();
                    await _sessionManager.StartSessionAsync(ClubId, request.MacAddress, endTime, client.Username);

                    if (ClubHub.TryGetPcConnection(ClubId, request.MacAddress, out var connId) && connId != null)
                    {
                        await _hubContext.Clients.Client(connId).SendAsync(SignalRMethods.ReceiveUnlock, endTime);
                    }

                    return Ok(new
                    {
                        token = tokenString,
                        role = "User",
                        username = client.Username,
                        balance = client.Balance,
                        totalSpent = client.TotalSpent,
                        discountPercent,
                        avatarUrl = client.AvatarUrl,
                        hasActiveSession = true,
                        endTime = endTime,
                        message = $"Восстановлено {minutesToRestore} минут"
                    });
                }
            }

            return Ok(new
            {
                token = tokenString,
                role = "User",
                username = client.Username,
                balance = client.Balance,
                totalSpent = client.TotalSpent,
                discountPercent,
                avatarUrl = client.AvatarUrl,
                hasActiveSession = false
            });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен, попробуйте позже.");

            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest("Email обязателен.");

            var client = await NetworkClients.FirstOrDefaultAsync(c => c.Email != "" && c.Email == request.Email);
            if (client == null) return BadRequest("Пользователь с таким Email не найден.");

            var code = new Random().Next(100000, 999999).ToString();
            client.ResetCode = code;
            client.ResetCodeExpiry = DateTime.UtcNow.AddMinutes(15);
            await _platform.SaveChangesAsync();

            string subject = "Восстановление пароля - SPIK Club";
            string body = $@"
                <h3>Код восстановления пароля</h3>
                <p>Ваш код: <b>{code}</b></p>
                <p>Код действителен 15 минут.</p>";

            try
            {
                await _emailService.SendEmailAsync(client.Email, subject, body);
                Console.WriteLine($" >>> КОД (Debug): {code} <<<");
                return Ok(new { message = "Код отправлен на вашу почту" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка отправки письма: {ex.Message}");
            }
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен, попробуйте позже.");

            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest("Email обязателен.");

            var client = await NetworkClients.FirstOrDefaultAsync(c => c.Email != "" && c.Email == request.Email);
            if (client == null) return BadRequest("Пользователь не найден.");

            if (client.ResetCode != request.Code || client.ResetCodeExpiry < DateTime.UtcNow)
                return BadRequest("Неверный код.");

            client.PasswordHash = PasswordHasher.Hash(request.NewPassword);
            client.ResetCode = null;
            client.ResetCodeExpiry = null;

            await _platform.SaveChangesAsync();
            return Ok(new { message = "Пароль изменен" });
        }

        [Authorize]
        [HttpPost("buy")]
        public async Task<IActionResult> BuyTariff([FromBody] BuyRequest request)
        {
            if (!HasNetwork) return StatusCode(503, "Клуб недоступен, попробуйте позже.");

            var client = await NetworkClients
                .Include(c => c.Group)
                .FirstOrDefaultAsync(c => c.Username == request.Username);
            if (client == null) return Unauthorized();

            var tariff = await _context.Tariffs.FindAsync(request.TariffId);
            if (tariff == null) return BadRequest("Тариф не найден");

            // Проверка времени
            if (tariff.StartHour.HasValue && tariff.EndHour.HasValue)
            {
                int currentHour = DateTime.Now.Hour;
                bool isAllowed = (tariff.StartHour < tariff.EndHour)
                    ? (currentHour >= tariff.StartHour && currentHour < tariff.EndHour)
                    : (currentHour >= tariff.StartHour || currentHour < tariff.EndHour);
                if (!isAllowed) return BadRequest($"Тариф доступен с {tariff.StartHour}:00 до {tariff.EndHour}:00");
            }

            // === РАСЧЕТ СКИДКИ (прогрессивная система) ===
            // Траты считаются по всей сети, а условия берутся у клуба, где идёт покупка.
            var club = await _platform.Clubs.FirstOrDefaultAsync(c => c.Id == ClubId);
            int discountPercent = Loyalty.EffectiveDiscount(client, club);
            decimal finalPrice = Loyalty.ApplyDiscount(tariff.Price, discountPercent);
            // ======================================

            if (client.Balance < finalPrice)
                return BadRequest($"Недостаточно средств. Нужно: {finalPrice:N0}, есть: {client.Balance:N0}");

            // Логика времени
            var computer = await _context.Computers.FirstOrDefaultAsync(c => c.Name == request.MacAddress);
            DateTime unlockUntil;
            var savesRemaining = !tariff.IsBurnable;

            if (tariff.IsFixedTime && tariff.EndHour.HasValue)
            {
                DateTime nowLocal = DateTime.Now;
                DateTime targetTime = nowLocal.Date.AddHours(tariff.EndHour.Value);
                if (targetTime <= nowLocal) targetTime = targetTime.AddDays(1);
                unlockUntil = targetTime.ToUniversalTime();
            }
            else
            {
                if (computer != null && computer.CurrentUser == client.Username && computer.SessionEndTime.HasValue && computer.SessionEndTime.Value > DateTime.UtcNow)
                {
                    unlockUntil = computer.SessionEndTime.Value.AddMinutes(tariff.DurationMinutes);
                    // Уже идёт сессия: если хоть раз купили несгораемый — остаток сохраняем.
                    savesRemaining = computer.SessionSavesRemaining || savesRemaining;
                }
                else
                    unlockUntil = DateTime.UtcNow.AddMinutes(tariff.DurationMinutes);
            }

            // Списание и начисление трат
            client.Balance -= finalPrice;
            client.TotalSpent += finalPrice;
            await _platform.SaveChangesAsync();

            // Баланс и сессия лежат в разных базах, общей транзакции нет. Если выдать
            // время не удалось, деньги возвращаем — иначе посетитель заплатил впустую.
            try
            {
                if (computer != null)
                {
                    computer.CurrentUser = client.Username;
                    computer.SessionEndTime = unlockUntil;
                    computer.SessionSavesRemaining = savesRemaining;
                    computer.CurrentTariffName = tariff.Name;
                    computer.IsOnline = true;
                }

                await _sessionManager.StartSessionAsync(ClubId, request.MacAddress, unlockUntil, client.Username);
                await _context.SaveChangesAsync();
            }
            catch
            {
                client.Balance += finalPrice;
                client.TotalSpent -= finalPrice;
                if (client.TotalSpent < 0) client.TotalSpent = 0;
                await _platform.SaveChangesAsync();
                throw;
            }

            if (ClubHub.TryGetPcConnection(ClubId, request.MacAddress, out var connectionId) && connectionId != null)
            {
                await _hubContext.Clients.Client(connectionId).SendAsync(SignalRMethods.ReceiveUnlock, unlockUntil);
            }

            await _balance.PushAsync(client.Username, client.Balance);

            return Ok(new { message = "Куплено", newBalance = client.Balance, endTime = unlockUntil });
        }

        /// <summary>
        /// Посетитель завершает свою сессию сам. Раньше шелл дёргал для этого
        /// админский /api/Admin/stop, что требовало прав администратора зала.
        /// </summary>
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> LogoutSession([FromQuery] string pcId)
        {
            if (string.IsNullOrWhiteSpace(pcId)) return BadRequest(new { error = "pcId обязателен" });

            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var computer = await _context.Computers.FirstOrDefaultAsync(c => c.Name == pcId);

            // Останавливаем только свою сессию: иначе любой посетитель мог бы
            // выкинуть соседа, зная имя его ПК.
            if (computer == null || computer.CurrentUser != username)
                return Forbid();

            // Недоигранное время — только с несгораемого пакета (SessionSavesRemaining).
            var remainingMinutes = 0;
            if (computer.SessionSavesRemaining
                && computer.SessionEndTime.HasValue
                && computer.SessionEndTime.Value > DateTime.UtcNow)
            {
                remainingMinutes = (int)(computer.SessionEndTime.Value - DateTime.UtcNow).TotalMinutes;

                if (remainingMinutes > 0 && HasNetwork)
                {
                    var client = await NetworkClients.FirstOrDefaultAsync(c => c.Username == username);
                    if (client != null)
                    {
                        client.RemainingMinutes += remainingMinutes;
                        await _platform.SaveChangesAsync();
                    }
                }
            }

            computer.CurrentUser = null;
            computer.SessionEndTime = null;
            computer.SessionSavesRemaining = true;
            computer.CurrentTariffName = null;
            computer.Status = ComputerStatus.Locked;

            await _sessionManager.StopSessionAsync(ClubId, pcId);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Сессия завершена", savedMinutes = remainingMinutes });
        }

        /// <summary>Карта клуба для шелла: свободные / занятые / оффлайн ПК.</summary>
        [Authorize]
        [HttpGet("computers-map")]
        public async Task<IActionResult> GetComputersMap([FromQuery] string? currentPc)
        {
            var pcs = await _context.Computers
                .AsNoTracking()
                .Where(c => c.IsApproved)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var result = pcs.Select(pc =>
            {
                string availability;
                if (pc.Status == ComputerStatus.Error || !pc.IsOnline)
                    availability = "Offline";
                else if (!string.IsNullOrEmpty(pc.CurrentUser) || pc.Status == ComputerStatus.Active)
                    availability = "Busy";
                else
                    availability = "Free";

                return new
                {
                    id = pc.Id,
                    name = pc.Name,
                    displayName = string.IsNullOrEmpty(pc.DisplayName) ? pc.Name : pc.DisplayName,
                    groupName = pc.GroupName ?? "Общий зал",
                    availability,
                    isCurrent = !string.IsNullOrEmpty(currentPc)
                        && string.Equals(pc.Name, currentPc, StringComparison.OrdinalIgnoreCase),
                    mapX = pc.MapX,
                    mapY = pc.MapY
                };
            });

            return Ok(result);
        }

        /// <summary>Пересадка: перенос активной сессии на другой свободный ПК клуба.</summary>
        [Authorize]
        [HttpPost("transfer")]
        public async Task<IActionResult> TransferSession([FromBody] TransferSessionRequest request)
        {
            if (request == null
                || string.IsNullOrWhiteSpace(request.FromPcName)
                || string.IsNullOrWhiteSpace(request.ToPcName))
                return BadRequest(new { error = "Укажите текущий и целевой ПК" });

            if (string.Equals(request.FromPcName, request.ToPcName, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = "Вы уже на этом компьютере" });

            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var from = await _context.Computers.FirstOrDefaultAsync(c => c.Name == request.FromPcName);
            var to = await _context.Computers.FirstOrDefaultAsync(c => c.Name == request.ToPcName);
            if (from == null || to == null) return NotFound(new { error = "Компьютер не найден" });
            if (!to.IsApproved) return BadRequest(new { error = "Целевой ПК не подтверждён" });

            if (from.CurrentUser != username)
                return Forbid();

            if (!from.SessionEndTime.HasValue || from.SessionEndTime.Value <= DateTime.UtcNow)
                return BadRequest(new { error = "Нет активной сессии для переноса" });

            if (!string.IsNullOrEmpty(to.CurrentUser) || to.Status == ComputerStatus.Active)
                return BadRequest(new { error = "Целевой компьютер занят" });

            if (!to.IsOnline)
                return BadRequest(new { error = "Целевой компьютер оффлайн" });

            var endTime = from.SessionEndTime.Value;
            var saves = from.SessionSavesRemaining;
            var tariff = from.CurrentTariffName;

            // Освобождаем текущий
            from.CurrentUser = null;
            from.SessionEndTime = null;
            from.SessionSavesRemaining = true;
            from.CurrentTariffName = null;
            from.Status = ComputerStatus.Locked;
            await _sessionManager.StopSessionAsync(ClubId, from.Name);

            // Занимаем целевой
            to.CurrentUser = username;
            to.SessionEndTime = endTime;
            to.SessionSavesRemaining = saves;
            to.CurrentTariffName = tariff;
            to.Status = ComputerStatus.Active;
            to.IsOnline = true;
            await _sessionManager.StartSessionAsync(ClubId, to.Name, endTime, username);

            await _context.SaveChangesAsync();

            if (ClubHub.TryGetPcConnection(ClubId, from.Name, out var fromConn) && fromConn != null)
                await _hubContext.Clients.Client(fromConn).SendAsync(SignalRMethods.ReceiveLock);

            if (ClubHub.TryGetPcConnection(ClubId, to.Name, out var toConn) && toConn != null)
                await _hubContext.Clients.Client(toConn).SendAsync(SignalRMethods.ReceiveUnlock, endTime);

            var toLabel = string.IsNullOrEmpty(to.DisplayName) ? to.Name : to.DisplayName;
            return Ok(new
            {
                message = "Сессия перенесена",
                targetPc = to.Name,
                targetDisplayName = toLabel,
                endTime
            });
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus([FromQuery] string mac)
        {
            if (string.IsNullOrEmpty(mac)) return BadRequest();

            var session = await _sessionManager.GetActiveSessionAsync(ClubId, mac);
            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == mac);
            string pcDisplayName = pc?.DisplayName ?? pc?.Name ?? "PC";

            var club = await _platform.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ClubId);
            bool enableShop = club?.EnableShop ?? true;

            if (session != null && session.EndTime > DateTime.UtcNow)
            {
                string username = pc?.CurrentUser ?? "";
                decimal balance = 0;
                string? avatarUrl = null;
                if (!string.IsNullOrEmpty(username) && HasNetwork)
                {
                    var row = await NetworkClients
                        .Where(c => c.Username == username)
                        .Select(c => new { c.Balance, c.AvatarUrl })
                        .FirstOrDefaultAsync();
                    if (row != null)
                    {
                        balance = row.Balance;
                        avatarUrl = row.AvatarUrl;
                    }
                }

                return Ok(new
                {
                    IsActive = true,
                    EndTime = session.EndTime,
                    Username = username,
                    Balance = balance,
                    PcName = pcDisplayName,
                    EnableShop = enableShop,
                    AvatarUrl = avatarUrl,
                    TariffName = pc?.CurrentTariffName
                });
            }

            return Ok(new
            {
                IsActive = false,
                EndTime = DateTime.MinValue,
                Balance = 0,
                PcName = pcDisplayName,
                EnableShop = enableShop,
                AvatarUrl = (string?)null,
                TariffName = (string?)null
            });
        }

        // Вспомогательный метод для проверки Email
        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                // Используем System.Net.Mail для проверки формата
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }

    public class StaffChangePasswordRequest
    {
        public string? CurrentPassword { get; set; }
        public string NewPassword { get; set; } = string.Empty;
    }
}
