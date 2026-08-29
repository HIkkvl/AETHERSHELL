namespace AetherShell.Server.Models
{
    /// <summary>
    /// Группа посетителей сети (VIP, скидка, постоянные и т.п.).
    /// Общая на все филиалы владельца — как сами клиенты.
    /// </summary>
    public class ClientGroup
    {
        public int Id { get; set; }
        public int NetworkId { get; set; }
        public Account Network { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        /// <summary>Цвет метки в панели (#RRGGBB).</summary>
        public string Color { get; set; } = "#6B7280";

        /// <summary>
        /// Фиксированная скидка группы. Если задана — используется вместо лояльности
        /// (ручной DiscountOverride у клиента всё равно сильнее).
        /// </summary>
        public int? DiscountPercent { get; set; }

        public int SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
