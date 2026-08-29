using AetherShell.Server.Data;
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

            context.Result = new BadRequestObjectResult(new
            {
                error = "Клуб не указан. Передайте заголовок X-Club-Id (веб-панель) или X-Club-Key (шелл)."
            });
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}
