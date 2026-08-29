using System;
using System.Collections.Generic;

namespace AetherShell.Server.Models
{
    public static class AccountRoles
    {
        /// <summary>Владелец платформы (ты). Видит и администрирует все клубы.</summary>
        public const string PlatformAdmin = "PlatformAdmin";

        /// <summary>Клиент — владелец одного клуба или сети клубов.</summary>
        public const string Owner = "Owner";
    }

    /// <summary>
    /// Платформенный аккаунт: логин, который выдаётся клиенту при подключении.
    /// Не путать с <see cref="User"/> — тот живёт внутри клуба (персонал и посетители).
    /// </summary>
    public class Account
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = AccountRoles.Owner;
        public string DisplayName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        /// <summary>Требовать смену пароля при первом входе (пароль сгенерирован при создании клуба).</summary>
        public bool MustChangePassword { get; set; }

        public ICollection<Club> Clubs { get; set; } = new List<Club>();
    }
}
