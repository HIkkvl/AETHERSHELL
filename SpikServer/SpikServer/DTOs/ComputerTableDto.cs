namespace AetherShell.Server.DTOs
{
    public class ComputerTableDto
    {
        public int Id { get; set; }
        public string NameToDisplay { get; set; } = "";
        public string PcName { get; set; } = "";
        public string GroupName { get; set; } = "Общий зал";
        public bool IsOnline { get; set; }
        public string? CurrentUser { get; set; }
        public DateTime? SessionEndTime { get; set; }
        
        // Новые поля
        public string Status { get; set; } = "Offline";
        public bool IsApproved { get; set; } = true;
        public DateTime? LastSeenAt { get; set; }

        // Что запущено прямо сейчас: панель показывает название вместо «в игре».
        public string? CurrentApp { get; set; }
        public string? CurrentAppTitle { get; set; }
        public DateTime? CurrentAppSince { get; set; }

        /// <summary>Позиция на карте клуба (0…100). null — автосетка.</summary>
        public double? MapX { get; set; }
        public double? MapY { get; set; }
    }

    public class ComputerLayoutRequest
    {
        public List<ComputerLayoutItem> Items { get; set; } = new();
    }

    public class ComputerLayoutItem
    {
        public int Id { get; set; }
        public double MapX { get; set; }
        public double MapY { get; set; }
    }
}