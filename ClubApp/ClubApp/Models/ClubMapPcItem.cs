namespace AetherShell.Client.Models
{
    public class ClubMapPcItem
    {
        public int id { get; set; }
        public string name { get; set; }
        public string displayName { get; set; }
        public string groupName { get; set; }
        /// <summary>Free | Busy | Offline</summary>
        public string availability { get; set; }
        public bool isCurrent { get; set; }
        public double? mapX { get; set; }
        public double? mapY { get; set; }

        public string StatusLabel
        {
            get
            {
                if (isCurrent) return "Вы здесь";
                switch ((availability ?? "").ToLowerInvariant())
                {
                    case "free": return "Свободен";
                    case "busy": return "Занят";
                    default: return "Оффлайн";
                }
            }
        }
    }
}
