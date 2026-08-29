using System;

namespace AetherShell.Client.Models
{
    public class SessionStatusDto
    {
        public bool IsActive { get; set; }
        public DateTime EndTime { get; set; }
        public string Username { get; set; }
        public decimal Balance { get; set; }
        public string PcName { get; set; }

        /// <summary>Флаг с Auth/status (JSON camelCase enableShop).</summary>
        public bool enableShop { get; set; } = true;
        public string avatarUrl { get; set; }
        public string tariffName { get; set; }
    }
}
