namespace AetherShell.Server.DTOs
{
    public class LoginRequest
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public int? TariffId { get; set; }
    }

    public class RegisterDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    public class BuyRequest
    {
        public string Username { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public int TariffId { get; set; }
    }

    public class TransferSessionRequest
    {
        public string FromPcName { get; set; } = "";
        public string ToPcName { get; set; } = "";
    }

    public class ForgotPasswordDto
    {
        public string Email { get; set; } = "";
    }

    public class ResetPasswordDto
    {
        public string Email { get; set; } = "";
        public string Code { get; set; } = "";
        public string NewPassword { get; set; } = "";
    }

}
