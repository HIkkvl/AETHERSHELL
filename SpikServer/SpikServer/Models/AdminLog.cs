using System;

namespace AetherShell.Server.Models
{
    public class AdminLog
    {
        public int Id { get; set; }
        public string AdminName { get; set; } = string.Empty; 
        public string ActionType { get; set; } = string.Empty; 
        public string Target { get; set; } = string.Empty;  
        public string Details { get; set; } = string.Empty;  
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}