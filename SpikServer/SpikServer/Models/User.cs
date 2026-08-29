using System.ComponentModel.DataAnnotations.Schema;

namespace AetherShell.Server.Models
{
    /// <summary>
    /// Персонал зала: администратор, старший администратор, управляющий. Живёт в
    /// базе своего клуба, поэтому доступ сотрудника не распространяется на другие
    /// филиалы сети.
    ///
    /// Посетители здесь больше не хранятся — у них общий на сеть баланс, см.
    /// <see cref="Client"/>.
    /// </summary>
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;

        // ВАЖНО: Мы переименовали Password в PasswordHash
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Role { get; set; } = "Admin";

        public string Email { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
    }

    public class RegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
