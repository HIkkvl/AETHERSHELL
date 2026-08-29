namespace AetherShell.Server.Data
{
    public class Session
    {
        public int Id { get; set; }

        // Добавляем = "";
        public string ComputerName { get; set; } = "";

        /// <summary>
        /// Кто играл. Нужно для истории сессий в профиле клиента: раньше сессия
        /// знала только имя ПК, и связать её с пользователем было невозможно.
        /// </summary>
        public string? Username { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public bool IsActive { get; set; }
        public double Price { get; set; }
    }
}
