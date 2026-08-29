namespace AetherShell.Server.Data
{
    public class AppItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string ExePath { get; set; } = ""; 
        public string ImageUrl { get; set; } = ""; 
        public string Arguments { get; set; } = ""; 
        public string Category { get; set; } = "Game";
        public string? Genre { get; set; }
    }
}
