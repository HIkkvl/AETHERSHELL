using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.DTOs;
using AetherShell.Server.Filters;
using AetherShell.Server.Models;
using AetherShell.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using AetherShell.Server.Hubs;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace AetherShell.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RequireClub]
    public class OrdersController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly PlatformDbContext _platform;
        private readonly AuditLogger _logger;
        private readonly IHubContext<ClubHub> _hubContext;
        private readonly ServerSettings _serverSettings;
        private readonly ICurrentClub _currentClub;
        private readonly BalanceNotifier _balance;
        private readonly ClubRealtimeNotifier _live;

        public OrdersController(
            ClubDbContext context,
            PlatformDbContext platform,
            AuditLogger logger,
            IHubContext<ClubHub> hubContext,
            ServerSettings serverSettings,
            ICurrentClub currentClub,
            BalanceNotifier balance,
            ClubRealtimeNotifier live)
        {
            _context = context;
            _platform = platform;
            _logger = logger;
            _hubContext = hubContext;
            _serverSettings = serverSettings;
            _currentClub = currentClub;
            _balance = balance;
            _live = live;
        }

        /// <summary>Клуб текущего запроса. Гарантирован атрибутом <see cref="RequireClubAttribute"/>.</summary>
        private int ClubId => _currentClub.ClubId!.Value;

        /// <summary>Сеть клуба: баланс посетителя общий на все её филиалы.</summary>
        private IQueryable<Client> NetworkClients => _platform.Clients.Where(c => c.NetworkId == _currentClub.NetworkId);

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
        {
            if (dto.Items == null || !dto.Items.Any()) return BadRequest("Корзина пуста");
            if (_currentClub.NetworkId == null) return StatusCode(503, "Клуб недоступен, попробуйте позже.");

            var client = await NetworkClients
                .Include(c => c.Group)
                .FirstOrDefaultAsync(c => c.Username == dto.Username);
            if (client == null) return NotFound("Пользователь не найден");

            var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == dto.MacAddress);
            string pcName = pc?.DisplayName ?? dto.MacAddress;

            decimal originalSum = 0;
            var orderItems = new List<OrderItem>();
            var itemDetails = "";
            var stockHits = new List<(Product Product, int Qty)>();

            foreach (var itemDto in dto.Items)
            {
                var product = await _context.Products.FindAsync(itemDto.ProductId);
                if (product == null) continue;
                if (itemDto.Quantity <= 0) continue;

                if (product.StockQty < itemDto.Quantity)
                    return BadRequest($"Недостаточно «{product.Name}» на складе (осталось {product.StockQty})");

                originalSum += product.Price * itemDto.Quantity;

                orderItems.Add(new OrderItem
                {
                    ProductId = product.Id,
                    Quantity = itemDto.Quantity,
                    PriceAtMoment = product.Price,
                    ProductNameSnapshot = product.Name
                });

                stockHits.Add((product, itemDto.Quantity));
                itemDetails += $"{product.Name} x{itemDto.Quantity}, ";
            }

            if (originalSum == 0) return BadRequest("Некорректный заказ");

            // === РАСЧЕТ СКИДКИ (прогрессивная система) ===
            // Траты копятся по всей сети, условия берём у клуба, где делается заказ.
            var club = await _platform.Clubs.FirstOrDefaultAsync(c => c.Id == ClubId);
            int discountPercent = Loyalty.EffectiveDiscount(client, club);
            decimal finalSum = Loyalty.ApplyDiscount(originalSum, discountPercent);
            // ====================================

            if (client.Balance < finalSum)
            {
                return BadRequest($"Недостаточно средств. Нужно: {finalSum}, есть: {client.Balance}");
            }

            client.Balance -= finalSum;
            client.TotalSpent += finalSum;
            await _platform.SaveChangesAsync();

            var order = new Order
            {
                ClientId = client.Id,
                Username = client.Username,
                PcName = pcName,
                TotalPrice = finalSum,
                Status = OrderStatus.New,
                Items = orderItems,
                CreatedAt = DateTime.UtcNow
            };

            // Баланс и заказ лежат в разных базах, общей транзакции нет: если заказ
            // не записался, деньги возвращаем.
            try
            {
                await using var tx = await _context.Database.BeginTransactionAsync();

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                foreach (var (product, qty) in stockHits)
                {
                    product.StockQty -= qty;
                    _context.StockMovements.Add(new StockMovement
                    {
                        ProductId = product.Id,
                        Delta = -qty,
                        BalanceAfter = product.StockQty,
                        Kind = StockMovementKind.Order,
                        OrderId = order.Id,
                        Reason = $"Заказ #{order.Id}",
                        CreatedBy = client.Username,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                client.Balance += finalSum;
                client.TotalSpent -= finalSum;
                if (client.TotalSpent < 0) client.TotalSpent = 0;
                await _platform.SaveChangesAsync();
                throw;
            }

            await _logger.LogAsync("System", "Shop", pcName, $"Заказ на {finalSum}₸ (Скидка {discountPercent}%): {itemDetails}");
            await _hubContext.Clients.Group(ClubHub.AdminGroup(ClubId)).SendAsync("ReceiveOrderUpdate", order.Id, "New");
            await _balance.PushAsync(client.Username, client.Balance);
            await _live.ProductsUpdatedAsync();

            return Ok(new { message = "Заказ принят", newBalance = client.Balance, OrderId = order.Id });
        }

        [HttpGet]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> GetOrders([FromQuery] string? status = null, [FromQuery] bool active = false)
        {
            var query = _context.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<OrderStatus>(status, true, out OrderStatus statusEnum))
                {
                    query = query.Where(o => o.Status == statusEnum);
                }
            }
            else if (active)
            {
                query = query.Where(o => o.Status == OrderStatus.New || o.Status == OrderStatus.Processing || o.Status == OrderStatus.Ready);
            }
            else if (status == null && !active)
            {
                query = query.Where(o => o.Status == OrderStatus.Completed || o.Status == OrderStatus.Cancelled)
                             .OrderByDescending(o => o.CreatedAt)
                             .Take(30);
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.Id,
                    o.PcName,
                    o.TotalPrice,
                    Status = o.Status.ToString(),
                    Time = o.CreatedAt.ToLocalTime().ToString("HH:mm"),
                    Items = o.Items.Select(i => new { i.Product.Name, i.Quantity }).ToList()
                })
                .ToListAsync();

            return Ok(orders);
        }

        [HttpGet("my")]
        [Authorize]
        public async Task<IActionResult> GetMyOrders()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Unauthorized();

            var orders = await _context.Orders
                .Where(o => o.Username == username)
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .OrderByDescending(o => o.CreatedAt)
                .Take(100)
                .Select(o => new
                {
                    o.Id,
                    o.TotalPrice,
                    Status = o.Status.ToString(),
                    Time = o.CreatedAt.ToLocalTime().ToString("HH:mm"),
                    Items = o.Items.Select(i => new { i.ProductNameSnapshot, i.Quantity }).ToList()
                })
                .ToListAsync();

            return Ok(orders);
        }

        [HttpPost("{id}/status")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> UpdateStatus(int id, [FromQuery] string status)
        {
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == id);

            if (order == null) return NotFound();

            if (Enum.TryParse(status, true, out OrderStatus newStatus))
            {
                Client? refundedClient = null;

                if (newStatus == OrderStatus.Cancelled && order.Status != OrderStatus.Cancelled)
                {
                    var client = await NetworkClients.FirstOrDefaultAsync(c => c.Id == order.ClientId);
                    if (client != null)
                    {
                        // Возврат средств
                        // order.TotalPrice уже содержит цену со скидкой (мы так сохранили в CreateOrder),
                        // поэтому возвращаем ровно то, что списали.
                        client.Balance += order.TotalPrice;

                        // Откат суммы трат в программе лояльности
                        client.TotalSpent -= order.TotalPrice;
                        if (client.TotalSpent < 0) client.TotalSpent = 0;

                        await _platform.SaveChangesAsync();
                        await _logger.LogAsync("System", "Refund", order.PcName, $"Возврат {order.TotalPrice} за заказ #{id}");
                        refundedClient = client;
                    }

                    // Возврат товаров на склад (один раз на отмену)
                    var alreadyRestored = await _context.StockMovements
                        .AnyAsync(m => m.OrderId == order.Id && m.Kind == StockMovementKind.OrderCancel);
                    if (!alreadyRestored)
                    {
                        var items = await _context.OrderItems
                            .Where(i => i.OrderId == order.Id)
                            .ToListAsync();
                        foreach (var item in items)
                        {
                            var product = await _context.Products.FindAsync(item.ProductId);
                            if (product == null) continue;
                            product.StockQty += item.Quantity;
                            _context.StockMovements.Add(new StockMovement
                            {
                                ProductId = product.Id,
                                Delta = item.Quantity,
                                BalanceAfter = product.StockQty,
                                Kind = StockMovementKind.OrderCancel,
                                OrderId = order.Id,
                                Reason = $"Отмена заказа #{order.Id}",
                                CreatedBy = User.Identity?.Name ?? "Admin",
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }

                order.Status = newStatus;
                await _context.SaveChangesAsync();

                if (refundedClient != null)
                {
                    await _balance.PushAsync(refundedClient.Username, refundedClient.Balance);
                }

                if (newStatus == OrderStatus.Cancelled)
                    await _live.ProductsUpdatedAsync();

                var adminName = User.Identity?.Name ?? "Admin";
                await _logger.LogAsync(adminName, "Shop", order.PcName, $"Заказ #{id} -> {status}");

                // Ищем компьютер по имени или отображаемому имени
                var pc = await _context.Computers.FirstOrDefaultAsync(c => c.Name == order.PcName || c.DisplayName == order.PcName);

                if (pc != null)
                {
                    await _hubContext.Clients
                        .Group(ClubHub.PcGroup(ClubId, pc.Name))
                        .SendAsync("OrderStatusUpdated", order.Id, newStatus.ToString());
                }

                await _hubContext.Clients
                    .Group(ClubHub.AdminGroup(ClubId))
                    .SendAsync("ReceiveOrderUpdate", order.Id, newStatus.ToString());
                return Ok();
            }
            return BadRequest("Неверный статус");
        }
    }
}