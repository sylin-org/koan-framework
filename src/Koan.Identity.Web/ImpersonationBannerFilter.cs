using Microsoft.AspNetCore.Mvc.Filters;
using Koan.Identity.Impersonation;

namespace Koan.Identity.Web;

/// <summary>
/// SEC-0007 D8 — auto-injects the impersonation banner signal (<see cref="ImpersonationClaims.BannerHeader"/> = the
/// real operator's subject) on MVC action responses while the principal carries an actor claim, so a UI can surface
/// "you are acting as …". (As an action filter it covers MVC endpoints; a middleware/OnStarting variant would also
/// cover static files and short-circuited auth responses — a follow-on.)
/// </summary>
public sealed class ImpersonationBannerFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (ImpersonationClaims.IsImpersonating(user))
            context.HttpContext.Response.Headers[ImpersonationClaims.BannerHeader] = ImpersonationClaims.ActorOf(user);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
