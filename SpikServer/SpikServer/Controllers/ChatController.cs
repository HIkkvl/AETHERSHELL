using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using AetherShell.Server.Data;
using AetherShell.Server.Filters;
using AetherShell.Server.Hubs;

namespace AetherShell.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [RequireClub]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ClubDbContext _context;
        private readonly IHubContext<ClubHub> _hubContext;
        private readonly ICurrentClub _currentClub;

        public ChatController(ClubDbContext context, IHubContext<ClubHub> hubContext, ICurrentClub currentClub)
        {
            _context = context;
            _hubContext = hubContext;
            _currentClub = currentClub;
        }

        /// <summary>Клуб текущего запроса. Гарантирован атрибутом <see cref="RequireClubAttribute"/>.</summary>
        private int ClubId => _currentClub.ClubId!.Value;

        [HttpGet("{pcName}")]
        public async Task<IActionResult> GetHistory(string pcName)
        {
            var msgs = await _context.ChatMessages
                .Where(m => m.PcName == pcName)
                .OrderBy(m => m.CreatedAt)
                .Take(50)
                .ToListAsync();

            return Ok(msgs);
        }

        [HttpDelete("{pcName}")]
        [Authorize(Roles = "Admin,Senior,Super")]
        public async Task<IActionResult> ClearHistory(string pcName)
        {
            var msgs = await _context.ChatMessages
                .Where(m => m.PcName == pcName)
                .ToListAsync();

            if (msgs.Any())
            {
                _context.ChatMessages.RemoveRange(msgs);
                await _context.SaveChangesAsync();
            }

            // Уведомляем только свой клуб: и админов, и сам ПК.
            await _hubContext.Clients
                .Groups(ClubHub.AdminGroup(ClubId), ClubHub.PcGroup(ClubId, pcName))
                .SendAsync("ChatCleared", pcName);

            return Ok(new { message = "Chat cleared" });
        }
    }
}
