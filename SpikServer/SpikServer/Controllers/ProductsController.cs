using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;

namespace AetherShell.Server.Controllers
{
    [Authorize(Roles = "Admin,Senior,Super")]
    [Route("api/[controller]")]
    [ApiController]
    [AetherShell.Server.Filters.RequireClub]
    public class ProductsController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly AuditLogger _logger;
        private readonly ClubRealtimeNotifier _live;

        public ProductsController(ClubDbContext context, AuditLogger logger, ClubRealtimeNotifier live)
        {
            _context = context;
            _logger = logger;
            _live = live;
        }

        /// <summary>Меню для шелла: только доступные. Остаток тоже отдаём — можно скрыть нулевые позже.</summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetProducts()
        {
            var products = await _context.Products
                .Where(p => p.IsAvailable)
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Name)
                .ToListAsync();
            return Ok(products);
        }

        /// <summary>Все товары для учёта склада, включая скрытые. Доступно обычному Admin.</summary>
        [HttpGet("inventory")]
        public async Task<IActionResult> GetInventory()
        {
            var products = await _context.Products
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Name)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Price,
                    p.ImageUrl,
                    p.IsAvailable,
                    p.StockQty
                })
                .ToListAsync();
            return Ok(products);
        }

        [HttpPost]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> CreateProduct([FromBody] Product product)
        {
            if (string.IsNullOrEmpty(product.Name) || product.Price <= 0)
                return BadRequest("Некорректные данные");

            if (product.StockQty < 0) product.StockQty = 0;

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            if (product.StockQty > 0)
            {
                _context.StockMovements.Add(new StockMovement
                {
                    ProductId = product.Id,
                    Delta = product.StockQty,
                    BalanceAfter = product.StockQty,
                    Kind = StockMovementKind.In,
                    Reason = "Начальный остаток",
                    CreatedBy = User.Identity?.Name ?? "Admin",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Shop", "Menu", $"Добавлен товар: {product.Name}");
            await _live.ProductsUpdatedAsync();

            return Ok(product);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Senior,Super")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound();

            product.IsAvailable = false;
            await _context.SaveChangesAsync();

            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Shop", "Menu", $"Скрыт товар: {product.Name}");
            await _live.ProductsUpdatedAsync();

            return Ok();
        }

        /// <summary>Приход (delta&gt;0), уход (delta&lt;0) или установка остатка (setTo).</summary>
        [HttpPost("{id}/stock")]
        public async Task<IActionResult> AdjustStock(int id, [FromBody] StockAdjustRequest request)
        {
            if (request == null)
                return BadRequest("Пустой запрос");

            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound("Товар не найден");

            int delta;
            StockMovementKind kind;
            var reason = (request.Reason ?? "").Trim();

            if (request.SetTo.HasValue)
            {
                if (request.SetTo.Value < 0)
                    return BadRequest("Остаток не может быть отрицательным");
                delta = request.SetTo.Value - product.StockQty;
                kind = StockMovementKind.Adjustment;
                if (string.IsNullOrEmpty(reason)) reason = "Корректировка остатка";
            }
            else
            {
                delta = request.Delta;
                if (delta == 0) return BadRequest("Укажите количество");
                if (product.StockQty + delta < 0)
                    return BadRequest($"Недостаточно на складе (сейчас {product.StockQty})");

                kind = delta > 0 ? StockMovementKind.In : StockMovementKind.Out;
                if (string.IsNullOrEmpty(reason))
                    reason = delta > 0 ? "Приход" : "Уход";
            }

            if (delta == 0)
                return Ok(new { product.Id, product.Name, product.StockQty, message = "Без изменений" });

            product.StockQty += delta;
            var movement = new StockMovement
            {
                ProductId = product.Id,
                Delta = delta,
                BalanceAfter = product.StockQty,
                Kind = kind,
                Reason = reason,
                CreatedBy = User.Identity?.Name ?? "Admin",
                CreatedAt = DateTime.UtcNow
            };
            _context.StockMovements.Add(movement);
            await _context.SaveChangesAsync();

            var action = delta > 0 ? "Приход" : "Уход";
            await _logger.LogAsync(User.Identity?.Name ?? "Admin", "Stock", product.Name,
                $"{action}: {Math.Abs(delta)} шт. Остаток: {product.StockQty}. {reason}");
            await _live.ProductsUpdatedAsync();

            return Ok(new
            {
                product.Id,
                product.Name,
                product.StockQty,
                movement = new
                {
                    movement.Id,
                    movement.Delta,
                    movement.BalanceAfter,
                    kind = movement.Kind.ToString(),
                    movement.Reason,
                    movement.CreatedBy,
                    movement.CreatedAt
                }
            });
        }

        [HttpGet("{id}/movements")]
        public async Task<IActionResult> GetMovements(int id, [FromQuery] int take = 50)
        {
            if (take < 1) take = 1;
            if (take > 200) take = 200;

            var exists = await _context.Products.AnyAsync(p => p.Id == id);
            if (!exists) return NotFound();

            var rows = await _context.StockMovements
                .Where(m => m.ProductId == id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(take)
                .Select(m => new
                {
                    m.Id,
                    m.Delta,
                    m.BalanceAfter,
                    kind = m.Kind.ToString(),
                    m.OrderId,
                    m.Reason,
                    m.CreatedBy,
                    m.CreatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpGet("movements")]
        public async Task<IActionResult> GetAllMovements([FromQuery] int take = 100)
        {
            if (take < 1) take = 1;
            if (take > 300) take = 300;

            var rows = await _context.StockMovements
                .Include(m => m.Product)
                .OrderByDescending(m => m.CreatedAt)
                .Take(take)
                .Select(m => new
                {
                    m.Id,
                    m.ProductId,
                    productName = m.Product != null ? m.Product.Name : "",
                    category = m.Product != null ? m.Product.Category : "",
                    m.Delta,
                    m.BalanceAfter,
                    kind = m.Kind.ToString(),
                    m.OrderId,
                    m.Reason,
                    m.CreatedBy,
                    m.CreatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }
    }

    public class StockAdjustRequest
    {
        /// <summary>Изменение: +10 приход, −3 уход.</summary>
        public int Delta { get; set; }

        /// <summary>Если задано — выставить абсолютный остаток.</summary>
        public int? SetTo { get; set; }

        public string? Reason { get; set; }
    }
}
