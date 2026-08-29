namespace AetherShell.Server.DTOs
{
    public class DashboardStats
    {
        public int TotalComputers { get; set; }
        public int EnabledComputers { get; set; }
        public int DisabledComputers { get; set; }
        public int ActiveComputers { get; set; }      // С активной сессией
        public int ErrorComputers { get; set; }       // Со статусом Error
        public int PendingComputers { get; set; }     // Ожидают подтверждения
        public int UsersCount { get; set; }
        public int AppsCount { get; set; }
        public int TopAppCount { get; set; }
        public string TopAppName { get; set; } = "";
        public decimal RevenueTotal { get; set; }
        public decimal RevenueKaspi { get; set; }
        public decimal RevenuePackages { get; set; }
    }
}