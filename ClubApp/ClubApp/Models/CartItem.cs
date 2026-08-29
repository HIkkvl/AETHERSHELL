using AetherShell.Client.Models;

namespace AetherShell.Client.Models
{
    public class CartItem
    {
        public ProductItem Product { get; set; }
        public int Quantity { get; set; }
        public decimal TotalPrice => Product?.Price * Quantity ?? 0;
    }
}
