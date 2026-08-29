using System.Linq;
using AetherShell.Server.Data;
using AetherShell.Server.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AetherShell.Server.Filters
{
    /// <summary>
    /// Требует, чтобы запрос выполнялся в контексте конкретного клуба.
    ///
    /// Без клуба непонятно, к какой базе подключаться: у каждого клуба своя.
    /// Поэтому клубные контроллеры обязаны получить X-Club-Id или X-Club-Key,
    /// иначе запрос отклоняется до первого обращения к базе.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireClubAttribute : Attribute, IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var currentClub = context.HttpContext.RequestServices.GetService(typeof(ICurrentClub)) as ICurrentClub;

            if (currentClub?.ClubId is > 0) return;

            var hasKey = !string.IsNullOrWhiteSpace(
                context.HttpContext.Request.Headers[ClubScopeMiddleware.ClubKeyHeader].FirstOrDefault());
            var hasId = !string.IsNullOrWhiteSpace(
                context.HttpContext.Request.Headers[ClubScopeMiddleware.ClubIdHeader].FirstOrDefault());

            var error = (hasKey || hasId)
                ? "Ключ или id клуба не найдены (клуб неактивен или CLUB_KEY в server.config устарел)."
                : "Клуб не указан. Передайте заголовок X-Club-Id (веб-панель) или X-Club-Key (шелл).";

            context.Result = new BadRequestObjectResult(new { error });
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
