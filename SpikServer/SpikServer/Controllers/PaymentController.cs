using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Hubs;
using AetherShell.Server.Services;
using AetherShell.Server.Constants;

namespace AetherShell.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RequireClub]
    public class PaymentController : ControllerBase
    {
        private readonly PlatformDbContext _platform;
        private readonly IHubContext<ClubHub> _hubContext;
        private readonly AuditLogger _logger;
        private readonly ICurrentClub _currentClub;

        public PaymentController(PlatformDbContext platform, IHubContext<ClubHub> hubContext, AuditLogger logger, ICurrentClub currentClub)
        {
            _platform = platform;
            _hubContext = hubContext;
            _logger = logger;
            _currentClub = currentClub;
        }

        /// <summary>Клуб текущего запроса. Гарантирован атрибутом <see cref="RequireClubAttribute"/>.</summary>
        private int ClubId => _currentClub.ClubId!.Value;

        // 1. ГЕНЕРАЦИЯ ССЫЛКИ НА ОПЛАТУ
        [HttpPost("create-link")]
        public IActionResult CreatePaymentLink([FromBody] PaymentRequest req)
        {
            // В РЕАЛЬНОСТИ: Тут запрос к API Kaspi/Банка и получаете их URL
            // ДЛЯ ТЕСТА: генерируем ссылку на нашу собственную фейковую страницу

            var orderId = Guid.NewGuid().ToString(); // Уникальный номер заказа

            // Адрес берём из самого запроса: сервер может стоять за реверс-прокси на своём домене.
            var origin = $"{Request.Scheme}://{Request.Host}";
            var query = QueryString.Create(new Dictionary<string, string?>
            {
                ["amount"] = req.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["username"] = req.Username,
                ["mac"] = req.MacAddress,
                ["orderId"] = orderId
            });

            return Ok(new { url = $"{origin}/api/payment/mock-page{query}" });
        }

        // 2. ФЕЙКОВАЯ СТРАНИЦА ОПЛАТЫ (HTML)
        // Это то, что увидит пользователь в WebView. 
        [HttpGet("mock-page")]
        public ContentResult GetMockPaymentPage([FromQuery] decimal amount, [FromQuery] string username, [FromQuery] string mac, [FromQuery] string orderId)
        {
            var safeUser = System.Net.WebUtility.HtmlEncode(username ?? "");
            var safeMac = System.Net.WebUtility.HtmlEncode(mac ?? "");
            var safeOrderId = System.Net.WebUtility.HtmlEncode(orderId ?? "");

            string html = $@"
            <html>
            <head>
                <style>
                    body {{ font-family: sans-serif; text-align: center; padding: 20px; background-color: #f0f2f5; }}
                    .card {{ background: white; padding: 20px; border-radius: 10px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 400px; margin: 0 auto; }}
                    h1 {{ color: #333; }}
                    .amount {{ font-size: 24px; color: #27ae60; font-weight: bold; margin: 20px 0; }}
                    .qr-placeholder {{ width: 200px; height: 200px; background: #ddd; margin: 0 auto; display: flex; align-items: center; justify-content: center; }}
                    .btn-pay {{ background: #e74c3c; color: white; border: none; padding: 15px 30px; font-size: 18px; border-radius: 5px; cursor: pointer; margin-top: 20px; width: 100%; }}
                </style>
            </head>
            <body>
                <div class='card'>
                    <h1>Пополнение баланса</h1>
                    <p>Пользователь: <b>{safeUser}</b></p>
                    <div class='amount'>{amount} ₸</div>
                    
                    <div class='qr-placeholder'>
                        (Здесь будет Kaspi QR)
                    </div>
                    <p>Отсканируйте QR код через приложение Kaspi.kz</p>

                    <button class='btn-pay' onclick=""simulateSuccess()""> [СИМУЛЯЦИЯ] Я ОПЛАТИЛ </button>
                </div>

                <script>
                    function simulateSuccess() {{
                        fetch('/api/payment/webhook-simulate', {{
                            method: 'POST',
                            headers: {{ 'Content-Type': 'application/json' }},
                            body: JSON.stringify({{ 
                                orderId: '{safeOrderId}', 
                                amount: {amount}, 
                                username: '{safeUser}',
                                mac: '{safeMac}'
                            }})
                        }}).then(res => {{
                            document.body.innerHTML = '<h1>✅ Оплата прошла успешно!</h1><p>Окно закроется автоматически...</p>';
                        }});
                    }}
                </script>
            </body>
            </html>";

            return Content(html, "text/html");
        }

        [Authorize(Roles = "Admin,Super")]
        [HttpPost("webhook-simulate")]
        public async Task<IActionResult> Webhook([FromBody] WebhookData data)
        {
            if (_currentClub.NetworkId == null) return StatusCode(503, "Клуб недоступен");

            // Баланс общий на сеть, поэтому пополнение идёт в платформенную базу.
            var client = await _platform.Clients
                .FirstOrDefaultAsync(c => c.NetworkId == _currentClub.NetworkId && c.Username == data.Username);
            if (client == null) return BadRequest("User not found");

            client.Balance += data.Amount;
            await _platform.SaveChangesAsync();

            await _logger.LogAsync("PaymentSystem", "Money", data.Username, $"Пополнение через QR: {data.Amount} ₸");

            if (ClubHub.TryGetPcConnection(ClubId, data.Mac, out var connId) && connId != null)
            {
                await _hubContext.Clients.Client(connId).SendAsync("PaymentSuccess", client.Balance);
            }

            return Ok();
        }
    }

    public class PaymentRequest
    {
        public decimal Amount { get; set; }
        public string Username { get; set; }
        public string MacAddress { get; set; }
    }

    public class WebhookData
    {
        public string OrderId { get; set; }
        public decimal Amount { get; set; }
        public string Username { get; set; }
        public string Mac { get; set; }
    }
}