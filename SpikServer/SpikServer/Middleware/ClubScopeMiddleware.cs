using System.Security.Claims;
using AetherShell.Server.Data;
using AetherShell.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AetherShell.Server.Middleware
{
    /// <summary>
    /// Определяет клуб текущего запроса и кладёт его в <see cref="ICurrentClub"/>.
    ///
    /// Клуб можно указать двумя способами:
    ///  - <c>X-Club-Id</c> — используют веб-панели, которые уже знают id клуба;
    ///  - <c>X-Club-Key</c> — используют шелл и установщик, у них есть только ключ из server.config.
    ///
    /// Права проверяются так:
    ///  - club-токен привязан к своему клубу, запрошенный клуб игнорируется;
    ///  - PlatformAdmin может работать в любом клубе;
    ///  - Owner — только в своих клубах, принадлежность проверяется в базе;
    ///  - анонимный запрос получает запрошенный клуб как есть. Это безопасно, потому что
    ///    без токена доступны только публичные данные клуба (тарифы, товары, баннеры,
    ///    экран логина), а всё остальное закрыто [Authorize].
    ///
    /// Вместе с клубом определяется его сеть (владелец): по ней выбираются посетители,
    /// у которых баланс общий на все филиалы.
    /// </summary>
    public class ClubScopeMiddleware
    {
        public const string ClubIdHeader = "X-Club-Id";
        public const string ClubKeyHeader = "X-Club-Key";
        public const string ClubIdQuery = "clubId";
        public const string ClubKeyQuery = "clubKey";

        private static readonly TimeSpan KeyCacheTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan NetworkCacheTtl = TimeSpan.FromMinutes(5);

        private readonly RequestDelegate _next;

        public ClubScopeMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, ICurrentClub currentClub, PlatformDbContext db, IMemoryCache cache)
        {
            var user = context.User;
            var isAuthenticated = user?.Identity?.IsAuthenticated == true;

            var tokenType = user?.FindFirst(AetherClaims.TokenType)?.Value;
            var role = user?.FindFirst(ClaimTypes.Role)?.Value;
            var isPlatformAdmin = isAuthenticated
                && tokenType == AetherClaims.TokenTypeAccount
                && role == AccountRoles.PlatformAdmin;

            int? accountId = null;
            if (isAuthenticated && int.TryParse(user?.FindFirst(AetherClaims.AccountId)?.Value, out var parsedAccountId))
                accountId = parsedAccountId;

            // Клубный токен всегда работает только в своём клубе.
            if (isAuthenticated && tokenType == AetherClaims.TokenTypeClub
                && int.TryParse(user?.FindFirst(AetherClaims.ClubId)?.Value, out var tokenClubId))
            {
                currentClub.Set(tokenClubId, false, null, await ResolveNetworkAsync(tokenClubId, db, cache));
                await _next(context);
                return;
            }

            var requestedClubId = await ResolveRequestedClubAsync(context, db, cache);

            if (requestedClubId == null)
            {
                currentClub.Set(null, isPlatformAdmin, accountId);
                await _next(context);
                return;
            }

            if (isPlatformAdmin)
            {
                currentClub.Set(requestedClubId, true, accountId, await ResolveNetworkAsync(requestedClubId.Value, db, cache));
                GrantClubStaffRoles(context);
                await _next(context);
                return;
            }

            if (accountId != null && role == AccountRoles.Owner)
            {
                var owns = await db.Clubs
                    .AsNoTracking()
                    .AnyAsync(c => c.Id == requestedClubId && c.OwnerId == accountId && c.IsActive);

                if (!owns)
                {
                    await DenyAsync(context, StatusCodes.Status403Forbidden);
                    return;
                }

                // Владелец и есть сеть, лишний запрос за её номером не нужен.
                currentClub.Set(requestedClubId, false, accountId, accountId);
                GrantClubStaffRoles(context);
                await _next(context);
                return;
            }

            if (!isAuthenticated)
            {
                // Анонимный доступ к публичным данным клуба и вход посетителя.
                currentClub.Set(requestedClubId, false, null, await ResolveNetworkAsync(requestedClubId.Value, db, cache));
                await _next(context);
                return;
            }

            // Авторизован, но роль не даёт права работать в клубах.
            await DenyAsync(context, StatusCodes.Status403Forbidden);
        }

        /// <summary>
        /// Внутри своего клуба владелец и платформенный админ получают полные права зала.
        /// Благодаря этому им не нужна отдельная строка в Users, а admin-panel
        /// работает со своими привычными [Authorize(Roles = "...")] без изменений.
        /// </summary>
        private static void GrantClubStaffRoles(HttpContext context)
        {
            var identity = context.User.Identity as ClaimsIdentity;
            if (identity == null) return;

            foreach (var staffRole in new[] { "Super", "Senior", "Admin" })
            {
                if (!context.User.IsInRole(staffRole))
                    identity.AddClaim(new Claim(ClaimTypes.Role, staffRole));
            }
        }

        private static async Task DenyAsync(HttpContext context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new { error = "Нет доступа к этому клубу" });
        }

        /// <summary>
        /// Сеть клуба — это его владелец. Результат кэшируется: значение меняется
        /// только при передаче клуба другому владельцу, а спрашивать базу на каждом
        /// запросе посетителя не хочется.
        /// </summary>
        private static async Task<int?> ResolveNetworkAsync(int clubId, PlatformDbContext db, IMemoryCache cache)
        {
            var cacheKey = "clubnet:" + clubId;
            if (cache.TryGetValue<int?>(cacheKey, out var cached))
                return cached;

            var ownerId = await db.Clubs
                .AsNoTracking()
                .Where(c => c.Id == clubId)
                .Select(c => (int?)c.OwnerId)
                .FirstOrDefaultAsync();

            cache.Set(cacheKey, ownerId, NetworkCacheTtl);
            return ownerId;
        }

        private static async Task<int?> ResolveRequestedClubAsync(HttpContext context, PlatformDbContext db, IMemoryCache cache)
        {
            var rawId = FirstNonEmpty(
                context.Request.Headers[ClubIdHeader].FirstOrDefault(),
                context.Request.Query[ClubIdQuery].FirstOrDefault());

            if (int.TryParse(rawId, out var clubId) && clubId > 0)
                return clubId;

            var rawKey = FirstNonEmpty(
                context.Request.Headers[ClubKeyHeader].FirstOrDefault(),
                context.Request.Query[ClubKeyQuery].FirstOrDefault());

            if (string.IsNullOrWhiteSpace(rawKey))
                return null;

            var key = rawKey.Trim();
            var cacheKey = "clubkey:" + key;
            if (cache.TryGetValue<int?>(cacheKey, out var cached))
                return cached;

            var resolved = await db.Clubs
                .AsNoTracking()
                .Where(c => c.EnrollmentKey == key && c.IsActive)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();

            cache.Set(cacheKey, resolved, KeyCacheTtl);
            return resolved;
        }

        private static string? FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    public static class ClubScopeMiddlewareExtensions
    {
        public static IApplicationBuilder UseClubScope(this IApplicationBuilder app)
            => app.UseMiddleware<ClubScopeMiddleware>();
    }
}
