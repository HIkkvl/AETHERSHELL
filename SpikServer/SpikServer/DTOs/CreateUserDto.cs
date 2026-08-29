namespace AetherShell.Server.DTOs
{
    public class CreateUserDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
        public string Email { get; set; } = string.Empty;
        public decimal Balance { get; set; } = 0;
    }
}