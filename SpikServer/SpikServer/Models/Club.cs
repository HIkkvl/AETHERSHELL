using System;

namespace AetherShell.Server.Models
{
    /// <summary>
    /// Компьютерный клуб — единица изоляции данных. У каждого клуба своя база
    /// (aether_club_{Id}), а эта запись живёт в платформенной базе и хранит только
    /// реестровые сведения и настройки.
    /// </summary>
    public class Club
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Сегмент URL админ-панели: /panel/{Slug}. Уникален в платформе.
        /// </summary>
        public string Slug { get; set; } = string.Empty;

        public int OwnerId { get; set; }
        public Account Owner { get; set; } = null!;

        /// <summary>
        /// Ключ, который прописывается в server.config шелла и по которому ПК определяет свой клуб.
        /// Отзывается перегенерацией — старые установки перестают подключаться.
        /// </summary>
        public string EnrollmentKey { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? City { get; set; }
        public string? Address { get; set; }

        /// <summary>Последняя активность любого ПК клуба — заменяет heartbeat от отдельного сервера.</summary>
        public DateTime? LastSeenAt { get; set; }

        // Система лояльности: раньше жила в server-settings.json, теперь настраивается на клуб.
        public decimal LoyaltyFirstThreshold { get; set; } = 50000;
        public decimal LoyaltyStep { get; set; } = 5000;
        public int MaxDiscountPercent { get; set; } = 20;

        /// <summary>Новые ПК требуют подтверждения администратором перед выдачей сессий.</summary>
        public bool RequireComputerApproval { get; set; } = true;

        /// <summary>Показывать вкладку магазина/еды в клиентском шелле.</summary>
        public bool EnableShop { get; set; } = true;
    }
}
