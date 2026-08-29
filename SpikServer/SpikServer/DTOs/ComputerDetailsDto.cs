namespace AetherShell.Server.DTOs
{
    public class ComputerDetailsDto
    {
        public int Id { get; set; }
        public string PcName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string GroupName { get; set; } = "";
        public bool IsOnline { get; set; }
        public string Status { get; set; } = "";
        public string? CurrentUser { get; set; }
        public DateTime? SessionEndTime { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? CurrentApp { get; set; }
        public string? CurrentAppTitle { get; set; }
        public DateTime? CurrentAppSince { get; set; }
        
        // Системная информация
        public string? IpAddress { get; set; }
        public string? MacAddress { get; set; }
        public string? CpuName { get; set; }
        public int? RamTotalMb { get; set; }
        public int? RamUsedMb { get; set; }
        public string? GpuName { get; set; }
        public string? DiskInfo { get; set; }
        public string? OsVersion { get; set; }
        public DateTime? SystemInfoUpdatedAt { get; set; }
    }
}
