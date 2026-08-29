using System;

namespace AetherShell.Server.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }

        public string PcName { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public bool IsFromAdmin { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}