using System;

namespace AetherShell.Client.Models 
{
    public class ChatMessage
    {
        public string Text { get; set; }

        public bool IsFromAdmin { get; set; }

        public DateTime Time { get; set; } = DateTime.Now;
    }
}