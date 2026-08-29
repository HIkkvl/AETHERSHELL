using System;

namespace AetherShell.Server.Models
{
    /// <summary>
    /// Посетитель клуба. Живёт в платформенной базе, а не в базе клуба, потому что
    /// баланс общий на всю сеть филиалов: клиент пополняется в одном зале и играет
    /// в другом под тем же логином.
    ///
    /// Сеть — это владелец (<see cref="NetworkId"/> указывает на его
    /// <see cref="Account"/>). Одиночный клуб с точки зрения кода такая же сеть,
    /// просто из одного клуба, поэтому отдельной ветки логики для него нет.
    ///
    /// Не путать с <see cref="User"/>: там теперь только персонал зала, и он
    /// остаётся привязанным к своему клубу.
    /// </summary>
    public class Client
    {
        public int Id { get; set; }

        /// <summary>Сеть, которой принадлежит клиент: <see cref="Account.Id"/> владельца.</summary>
        public int NetworkId { get; set; }
        public Account Network { get; set; } = null!;

        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public decimal Balance { get; set; }

        /// <summary>
        /// Несгораемый остаток минут: возвращается при остановке несгораемой сессии
        /// и восстанавливается при следующем входе.
        /// </summary>
        public int RemainingMinutes { get; set; }

        /// <summary>Накопленные траты по всей сети — из них считается скидка.</summary>
        public decimal TotalSpent { get; set; }

        /// <summary>
        /// Ручная скидка от администратора. Если задана — перекрывает расчёт по TotalSpent
        /// и скидку группы.
        /// </summary>
        public int? DiscountOverride { get; set; }

        /// <summary>Группа посетителя (VIP, скидка и т.п.).</summary>
        public int? GroupId { get; set; }
        public ClientGroup? Group { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>URL аватарки посетителя (/uploads/...).</summary>
        public string? AvatarUrl { get; set; }

        public string? ResetCode { get; set; }
        public DateTime? ResetCodeExpiry { get; set; }

        /// <summary>Клуб, в котором клиент зарегистрировался. Нужен только для статистики.</summary>
        public int? RegisteredClubId { get; set; }
    }
}
