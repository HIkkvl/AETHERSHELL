namespace AetherShell.Server.DTOs
{
    public class BannerDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string ClickUrl { get; set; } = string.Empty;
        public int Position { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateBannerDto
    {
        public string Title { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string ClickUrl { get; set; } = string.Empty;
        public int Position { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateBannerDto
    {
        public string? Title { get; set; }
        public string? ImageUrl { get; set; }
        public string? ClickUrl { get; set; }
        public int? Position { get; set; }
        public bool? IsActive { get; set; }
    }
}
