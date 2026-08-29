namespace AetherShell.Client.Models
{
    public class LoginResponse
    {
        public string token { get; set; }
        public string username { get; set; }

        /// <summary>Роль в клубе: User, Client, Admin, Senior или Super.</summary>
        public string role { get; set; }

        public decimal balance { get; set; }
        public bool hasActiveSession { get; set; }
        public decimal totalSpent { get; set; }
        /// <summary>Эффективная скидка с сервера (лояльность или ручная).</summary>
        public int discountPercent { get; set; }
        public string avatarUrl { get; set; }
    }
}
