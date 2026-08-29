using AetherShell.Server.Data;
using AetherShell.Server.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AetherShell.Server.Services
{
    /// <summary>Результат создания клуба: то, что нужно отдать клиенту один раз.</summary>
    public record ClubProvisionResult(
        int ClubId,
        string ClubName,
        string Slug,
        string EnrollmentKey,
        string OwnerEmail,
        string? Password,
        bool EmailSent);

    public class ClubProvisionRequest
    {
        public string Name { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string? OwnerName { get; set; }
        public string? OwnerPhone { get; set; }
        public string? City { get; set; }
        public string? Address { get; set; }
        public int? LeadId { get; set; }
    }

    /// <summary>
    /// Ошибка, из-за которой клуб создать нельзя (занятое название и подобное).
    /// Вызывающий превращает её в 4xx, не в 500.
    /// </summary>
    public class ClubProvisioningException : Exception
    {
        public ClubProvisioningException(string message) : base(message) { }
    }

    /// <summary>
    /// Создание и удаление клуба целиком: запись в реестре, отдельная база данных
    /// со своей схемой, стартовые тарифы, аккаунт владельца и письмо с доступом.
    ///
    /// Живёт отдельным сервисом, потому что вызывается из двух мест: кабинета
    /// (кнопка «Подключить клуб») и Telegram-бота (кнопка «Принять» на заявке).
    /// </summary>
    public class ClubProvisioningService
    {
        private readonly PlatformDbContext _platform;
        private readonly IClubDbConnectionFactory _connections;
        private readonly IClubDbContextFactory _clubDb;
        private readonly EmailService _email;

        public ClubProvisioningService(
            PlatformDbContext platform,
            IClubDbConnectionFactory connections,
            IClubDbContextFactory clubDb,
            EmailService email)
        {
            _platform = platform;
            _connections = connections;
            _clubDb = clubDb;
            _email = email;
        }

        public async Task<ClubProvisionResult> CreateAsync(ClubProvisionRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ClubProvisioningException("Название клуба обязательно");
            if (string.IsNullOrWhiteSpace(request.OwnerEmail))
                throw new ClubProvisioningException("Email владельца обязателен");

            var name = request.Name.Trim();
            var email = request.OwnerEmail.Trim().ToLowerInvariant();

            if (await _platform.Clubs.AnyAsync(c => c.Name == name, ct))
                throw new ClubProvisioningException("Клуб с таким названием уже есть");

            // Сеть клубов: у существующего владельца просто добавляется ещё один клуб,
            // новый пароль в этом случае не выдаётся.
            var owner = await _platform.Accounts.FirstOrDefaultAsync(a => a.Email == email, ct);
            string? generatedPassword = null;

            if (owner == null)
            {
                generatedPassword = PasswordHasher.GenerateReadablePassword();
                owner = new Account
                {
                    Email = email,
                    PasswordHash = PasswordHasher.Hash(generatedPassword),
                    Role = AccountRoles.Owner,
                    DisplayName = string.IsNullOrWhiteSpace(request.OwnerName) ? name : request.OwnerName!.Trim(),
                    Phone = request.OwnerPhone,
                    IsActive = true,
                    MustChangePassword = true,
                    CreatedAt = DateTime.UtcNow
                };
                _platform.Accounts.Add(owner);
            }

            var takenSlugs = await _platform.Clubs.Select(c => c.Slug).ToListAsync(ct);
            var slug = ClubSlug.EnsureUnique(ClubSlug.FromName(name), takenSlugs);

            var club = new Club
            {
                Name = name,
                Slug = slug,
                Owner = owner,
                City = request.City,
                Address = request.Address,
                EnrollmentKey = PasswordHasher.GenerateEnrollmentKey(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _platform.Clubs.Add(club);
            await _platform.SaveChangesAsync(ct);

            // Своя база данных под клуб. Если развернуть её не удалось, запись
            // в реестре бесполезна — откатываем, чтобы не остался клуб-призрак.
            try
            {
                await CreateDatabaseAsync(club.Id, ct);
                await SeedClubDatabaseAsync(club.Id, ct);
            }
            catch (Exception)
            {
                _platform.Clubs.Remove(club);
                await _platform.SaveChangesAsync(ct);
                throw;
            }

            if (request.LeadId is int leadId)
            {
                var lead = await _platform.Leads.FirstOrDefaultAsync(l => l.Id == leadId, ct);
                if (lead != null)
                {
                    lead.Status = LeadStatus.Accepted;
                    lead.CreatedClubId = club.Id;
                    await _platform.SaveChangesAsync(ct);
                }
            }

            var emailSent = false;
            if (generatedPassword != null && _email.IsConfigured)
            {
                try
                {
                    await _email.SendEmailAsync(owner.Email, $"Доступ в кабинет AetherShell — {club.Name}",
                        BuildWelcomeEmail(club.Name, owner.Email, generatedPassword));
                    emailSent = true;
                }
                catch (Exception ex)
                {
                    // Письмо — удобство, а не условие: пароль всё равно показан на экране.
                    Console.WriteLine($"[Clubs] Не удалось отправить письмо владельцу: {ex.Message}");
                }
            }

            return new ClubProvisionResult(club.Id, club.Name, club.Slug, club.EnrollmentKey, owner.Email, generatedPassword, emailSent);
        }

        /// <summary>
        /// Полное удаление клуба: база данных сносится целиком, поэтому чистить
        /// таблицы по одной больше не нужно.
        /// </summary>
        public async Task DeleteAsync(Club club, CancellationToken ct = default)
        {
            var ownerId = club.OwnerId;

            await _platform.Leads.Where(l => l.CreatedClubId == club.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.CreatedClubId, (int?)null), ct);

            _platform.Clubs.Remove(club);
            await _platform.SaveChangesAsync(ct);

            await DropDatabaseAsync(club.Id, ct);

            // Клиентский аккаунт без клубов больше не нужен.
            var ownerStillHasClubs = await _platform.Clubs.AnyAsync(c => c.OwnerId == ownerId, ct);
            if (!ownerStillHasClubs)
            {
                var owner = await _platform.Accounts.FirstOrDefaultAsync(
                    a => a.Id == ownerId && a.Role == AccountRoles.Owner, ct);
                if (owner != null)
                {
                    _platform.Accounts.Remove(owner);
                    await _platform.SaveChangesAsync(ct);
                }
            }
        }

        /// <summary>Накатывает схему и стартовые данные. Идемпотентна: годится и для существующей базы.</summary>
        public async Task SeedClubDatabaseAsync(int clubId, CancellationToken ct = default)
        {
            await EnsureDatabaseAsync(clubId, ct);

            await using var db = _clubDb.Create(clubId);
            await db.Database.MigrateAsync(ct);

            if (!await db.Tariffs.AnyAsync(ct))
            {
                db.Tariffs.AddRange(
                    new Tariff { Name = "1 час", DurationMinutes = 60, Price = 500, Feature1 = "Общий зал", Feature2 = "144Hz монитор" },
                    new Tariff { Name = "3 часа", DurationMinutes = 180, Price = 1300, Feature1 = "Общий зал", Feature2 = "144Hz монитор" },
                    new Tariff { Name = "5 часов", DurationMinutes = 300, Price = 2000, Feature1 = "Общий зал", Feature2 = "144Hz монитор" });
                await db.SaveChangesAsync(ct);
            }
        }

        /// <summary>
        /// Создаёт PostgreSQL-базу клуба, если её ещё нет (aether_club_{id}).
        /// Нужно и при провижининге, и на старте: запись в Clubs может остаться после сбоя/сброса тома.
        /// </summary>
        public async Task EnsureDatabaseAsync(int clubId, CancellationToken ct = default)
        {
            var dbName = _connections.DatabaseName(clubId);

            await using var connection = new NpgsqlConnection(_connections.PlatformConnectionString);
            await connection.OpenAsync(ct);

            await using var exists = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @name", connection);
            exists.Parameters.AddWithValue("name", dbName);
            if (await exists.ExecuteScalarAsync(ct) != null) return;

            // Имя собирается из числового Id, поэтому подстановка безопасна:
            // параметры в CREATE DATABASE PostgreSQL не поддерживает.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
            await create.ExecuteNonQueryAsync(ct);

            Console.WriteLine($"[Clubs] Создана база данных клуба: {dbName}");
        }

        private Task CreateDatabaseAsync(int clubId, CancellationToken ct)
            => EnsureDatabaseAsync(clubId, ct);

        private async Task DropDatabaseAsync(int clubId, CancellationToken ct)
        {
            var dbName = _connections.DatabaseName(clubId);

            // Пул держит открытые соединения к удаляемой базе, из-за них DROP не пройдёт.
            NpgsqlConnection.ClearAllPools();

            await using var connection = new NpgsqlConnection(_connections.PlatformConnectionString);
            await connection.OpenAsync(ct);

            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(ct);

            Console.WriteLine($"[Clubs] Удалена база данных клуба: {dbName}");
        }

        private static string BuildWelcomeEmail(string clubName, string email, string password) => $@"
            <h2>Доступ в кабинет AetherShell</h2>
            <p>Клуб: <b>{System.Net.WebUtility.HtmlEncode(clubName)}</b></p>
            <p>Логин: <b>{System.Net.WebUtility.HtmlEncode(email)}</b><br/>
               Пароль: <b>{System.Net.WebUtility.HtmlEncode(password)}</b></p>
            <p>Смените пароль при первом входе. В кабинете вы найдёте установщик шелла для своего клуба.</p>";
    }
}
