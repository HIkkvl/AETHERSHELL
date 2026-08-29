namespace AetherShell.Client.Models
{
    public class Banner
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ImageUrl { get; set; }
        public string ClickUrl { get; set; }
        public int Position { get; set; } // 1 - left, 2 - right
        public bool IsActive { get; set; }
    }
}
