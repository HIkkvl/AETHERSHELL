using System;

namespace AetherShell.Server.Models
{
    public class Banner
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty; 
        public string ImageUrl { get; set; } = string.Empty; 
        public string ClickUrl { get; set; } = string.Empty; 
        public int Position { get; set; } = 1; 
        public bool IsActive { get; set; } = true; 
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
