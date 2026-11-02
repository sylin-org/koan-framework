using System.Globalization;
using System.Text.Json;
using Koan.Web.Authorization;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Koan.Web.Filters;

/// <summary>What every <c>[Tool]</c> action answers to, on either door (ARCH-0141): the one gate before anything is read of the
/// request, a body read as a form or one JSON object alike, and a value that cannot bind refused with its field named.</summary>
internal sealed class ToolFilter : IAsyncAuthorizationFilter, IResourceFilter, IActionFilter
{
    public static readonly ToolFilter Instance = new();

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        // WEB-0083: a credential a handler rejected is not the anonymous case, whatever the gate would say to a visitor.
        if (http.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Failure is not null)
        {
            context.Result = Answers.Denied(http, AuthorizeDecision.Challenged());
            return;
        }

        var origins = http.RequestServices.GetService<IOptions<OriginOptions>>()?.Value ?? OriginOptions.Empty;
        var decision = await EntityAuthorization.Decide(
            http.RequestServices.GetService<IAuthorize>(),
            OriginStamp.Apply(http.User, OriginResolver.FromHttpContext(http, origins)),
            EntityAuthorization.ActionOf(http.Request.Method),
            ((ControllerActionDescriptor)context.ActionDescriptor).MethodInfo,
            http.Request.RouteValues,
            http.RequestAborted);
        if (decision is not null) context.Result = Answers.Denied(http, decision);
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        if (context.ActionDescriptor.Parameters.All(parameter => parameter.BindingInfo?.BindingSource != BindingSource.Body))
            context.ValueProviderFactories.Add(JsonFields.Instance);
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid) context.Result = Answers.Unbound(context.HttpContext, context.ModelState);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    /// <summary>A JSON object's members as form values, after the route's so that a body never names the address.</summary>
    private sealed class JsonFields : IValueProviderFactory
    {
        public static readonly JsonFields Instance = new();

        public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
        {
            var request = context.ActionContext.HttpContext.Request;
            if (!request.HasJsonContentType()) return;
            request.EnableBuffering();
            try
            {
                using var json = await JsonDocument.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                var fields = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
                Flatten(json.RootElement, "", fields);
                context.ValueProviders.Add(new JQueryFormValueProvider(BindingSource.Form, fields, CultureInfo.InvariantCulture));
            }
            catch (JsonException)
            {
                context.ActionContext.ModelState.TryAddModelError("", "The body is not one JSON object.");
            }
            finally
            {
                request.Body.Position = 0;
            }
        }

        private static void Flatten(JsonElement element, string name, Dictionary<string, StringValues> fields)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var member in element.EnumerateObject())
                        Flatten(member.Value, name.Length == 0 ? member.Name : $"{name}.{member.Name}", fields);
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray()) Flatten(item, $"{name}[{index++}]", fields);
                    break;
                case JsonValueKind.String:
                    fields[name] = element.GetString();
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break;
                default:
                    fields[name] = element.GetRawText();
                    break;
            }
        }
    }
}
