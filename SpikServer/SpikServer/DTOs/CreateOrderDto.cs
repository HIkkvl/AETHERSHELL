using System.Collections.Generic;

namespace AetherShell.Server.DTOs
{
    public class CartItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class CreateOrderDto
    {
        public string Username { get; set; }
        public string MacAddress { get; set; }
        public List<CartItemDto> Items { get; set; }
    }
}