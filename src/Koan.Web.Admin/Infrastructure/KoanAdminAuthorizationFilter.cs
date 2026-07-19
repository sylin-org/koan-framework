using Koan.Web.Admin.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminAuthorizationFilter(
    IAuthorizationService authorization,
    IOptions<KoanAdminOptions> options,
    IHostEnvironment environment) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var snapshot = options.Value;
        if (!environment.IsDevelopment() || !snapshot.Enabled)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var result = await authorization.AuthorizeAsync(
            context.HttpContext.User,
            resource: null,
            snapshot.Authorization.Policy);

        if (result.Succeeded)
        {
            return;
        }

        context.Result = context.HttpContext.User.Identity?.IsAuthenticated == true
            ? new ForbidResult()
            : new ChallengeResult();
    }
}
