namespace AetherShell.Client.Models
{
    public class OrderResponse
    {
        public string message { get; set; }
        public decimal newBalance { get; set; }
        public int OrderId { get; set; }
    }
}
