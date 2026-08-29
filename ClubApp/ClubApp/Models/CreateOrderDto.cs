using System.Collections.Generic;

namespace AetherShell.Client.Models
{
    public class CreateOrderDto
    {
        public string Username { get; set; }
        public string MacAddress { get; set; }
        public List<OrderItemDto> Items { get; set; }
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }
}
