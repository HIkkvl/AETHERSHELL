namespace AetherShell.Client.Models
{
    public class PaymentRequest
    {
        public decimal Amount { get; set; }
        public string Username { get; set; }
        public string MacAddress { get; set; }
    }
}
