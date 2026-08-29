namespace AetherShell.Server.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string ImageUrl { get; set; } = string.Empty;
        public bool IsAvailable { get; set; } = true;

        /// <summary>Текущий остаток на складе клуба.</summary>
        public int StockQty { get; set; }

        public ICollection<StockMovement> Movements { get; set; } = new List<StockMovement>();
    }

    public enum StockMovementKind
    {
        /// <summary>Приход от поставщика / закупка.</summary>
        In = 1,
        /// <summary>Ручной уход / списание / порча.</summary>
        Out = 2,
        /// <summary>Списание в заказ посетителя.</summary>
        Order = 3,
        /// <summary>Возврат на склад при отмене заказа.</summary>
        OrderCancel = 4,
        /// <summary>Корректировка остатка админом.</summary>
        Adjustment = 5,
    }

    /// <summary>Движение товара: приход, уход, заказ.</summary>
    public class StockMovement
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product? Product { get; set; }

        /// <summary>Изменение остатка: + приход, − уход.</summary>
        public int Delta { get; set; }

        public int BalanceAfter { get; set; }
        public StockMovementKind Kind { get; set; }
        public int? OrderId { get; set; }
        public string Reason { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
