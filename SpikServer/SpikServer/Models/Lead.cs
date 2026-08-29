using System;

namespace AetherShell.Server.Models
{
    public enum LeadStatus
    {
        Pending = 0,
        Accepted = 1,
        Rejected = 2
    }

    /// <summary>
    /// Заявка с лендинга. Обрабатывается либо кнопкой в кабинете платформы, либо
    /// инлайн-кнопкой в Telegram — во втором случае в <see cref="TelegramMessageId"/>
    /// хранится сообщение, которое бот потом редактирует.
    /// </summary>
    public class Lead
    {
        public int Id { get; set; }
        public string ClubName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Comment { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public LeadStatus Status { get; set; } = LeadStatus.Pending;
        public long? TelegramMessageId { get; set; }

        /// <summary>Клуб, созданный по этой заявке, если её уже обработали.</summary>
        public int? CreatedClubId { get; set; }
        public Club? CreatedClub { get; set; }
    }
}
