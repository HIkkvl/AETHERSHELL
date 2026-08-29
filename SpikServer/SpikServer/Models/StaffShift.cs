namespace AetherShell.Server.Models
{
    public enum StaffShiftEndReason
    {
        /// <summary>Сотрудник нажал «Завершить смену».</summary>
        Manual = 1,
        /// <summary>Выход из панели (logout).</summary>
        Logout = 2,
        /// <summary>Повторный вход — предыдущая открытая смена закрыта автоматически.</summary>
        Reauth = 3,
    }

    /// <summary>Учёт рабочего времени персонала зала (смены).</summary>
    public class StaffShift
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAt { get; set; }
        public StaffShiftEndReason? EndReason { get; set; }
    }
}
