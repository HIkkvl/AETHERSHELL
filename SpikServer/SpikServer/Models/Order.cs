using System;
using System.Collections.Generic;

namespace AetherShell.Server.Models
{
    public enum OrderStatus
    {
        New,        // Новый - В процессе
        Processing, // Готовится
        Ready,      // Готово
        Completed,  // Выдано
        Cancelled   // Отменен
    }

    public class Order
    {
        public int Id { get; set; }

        /// <summary>
        /// Покупатель из платформенной таблицы <see cref="Client"/>. Внешнего ключа
        /// нет намеренно: клиент лежит в другой базе, а PostgreSQL не умеет ссылаться
        /// между базами. Логин продублирован снимком, чтобы список заказов читался
        /// одним запросом к базе клуба.
        /// </summary>
        public int ClientId { get; set; }
        public string Username { get; set; } = string.Empty;

        public string PcName { get; set; } = string.Empty;    

        public decimal TotalPrice { get; set; } 
        public OrderStatus Status { get; set; } = OrderStatus.New;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}
