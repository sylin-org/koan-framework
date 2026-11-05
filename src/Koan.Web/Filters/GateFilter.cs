using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Web.Filters;

/// <summary>The one gate of a routed action, before anything is read of the request or bound to it (ARCH-0141, ARCH-0146): on every <c>[Tool]</c>
/// action and every action that declares access on itself or on the class it is reached through. It asks <see cref="IAuthorize"/> with the
/// action's <see cref="System.Reflection.MethodInfo"/> and the route values; a refusal answers as a command's refusal does, and an allow that
/// carries a value puts it on the request as a feature of its own type, so the action and whatever renders its answer read what the gate
/// found and nothing resolves it twice.</summary>
internal sealed class GateFilter : IAsyncAuthorizationFilter
{
    public static readonly GateFilter Instance = new();

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        // WEB-0083: a credential a handler rejected is not the anonymous case, whatever the gate would say to a visitor. A session
        // cookie that no longer holds was presented by nobody, and its handler reports no credential (SEC-0037).
        if (http.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Failure is not null)
        {
            context.Result = Answers.Denied(http, AuthorizeDecision.Challenged());
            return;
        }

        var origins = http.RequestServices.GetService<IOptions<OriginOptions>>()?.Value ?? OriginOptions.Empty;
        var decision = await EntityAuthorization.Decision(
            http.RequestServices.GetService<IAuthorize>(),
            OriginStamp.Apply(http.User, OriginResolver.FromHttpContext(http, origins)),
            EntityAuthorization.ActionOf(http.Request.Method),
            ((ControllerActionDescriptor)context.ActionDescriptor).MethodInfo,
            http.Request.RouteValues,
            http.RequestAborted);
        if (decision is AuthorizeDecision.Allow allowed)
        {
            if (allowed.Value is { } found) http.Features[found.GetType()] = found;
            return;
        }

        context.Result = Answers.Denied(http, decision);
    }

    /// <summary>Puts the gate on each action that declares access, as <c>[Tool]</c> puts it on each tool.</summary>
    internal sealed class Convention : IApplicationModelConvention, IConfigureOptions<MvcOptions>
    {
        public void Configure(MvcOptions options) => options.Conventions.Add(this);

        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers)
                foreach (var action in controller.Actions)
                    if (RouteAccess.Judged(action.ActionMethod, controller.ControllerType)) On(action);
        }

        internal static void On(ActionModel action)
        {
            if (!action.Filters.Contains(Instance)) action.Filters.Add(Instance);
        }
    }
}
