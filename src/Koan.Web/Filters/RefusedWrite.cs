using Koan.Web.Controllers;
using Koan.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Koan.Web.Filters;

/// <summary>
/// How a declared entity write is refused (WEB-0088, WEB-0089). A body that could not bind is Koan's one refusal, as every other refusal of
/// the write is: a JSON body as the operation refuses what it cannot be (<see cref="EntityOperation.Unfit(JToken?, JsonSerializer)"/>), one
/// that is not an object or a value that cannot be the member it names, with that field named; a form with each field it could not take
/// named. A write that came as a form answers its <see cref="Problem"/> with what the form sent as <see cref="Problem.Sent"/>; and where its
/// sender reads pages and its controller hands the page that form reads again (<c>RefusedPage</c>), it answers that page, with the
/// refusal's status and never cached. It runs whether or not the action ran, so a form its gate or its binding refused reads its page too.
/// </summary>
internal sealed class RefusedWrite : IAsyncAlwaysRunResultFilter
{
    public static readonly RefusedWrite Instance = new();

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (context.Result is ObjectResult { StatusCode: >= 400 } refused
            && (refused.Value is ValidationProblemDetails && !context.ModelState.IsValid
                ? await Unbound(context).ConfigureAwait(false)
                : request.HasFormContentType ? refused.Value as Problem : null) is { } problem)
        {
            if (request.HasFormContentType)
            {
                try
                {
                    var form = await request.ReadFormAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
                    problem = problem with { Sent = SentForm.Fields(form) };
                }
                catch (Exception unread) when (unread is InvalidDataException or BadHttpRequestException)
                {
                    // A form past its limits, or one that cannot be read, is refused as it is, with nothing of it kept.
                }
            }

            context.Result = await Page(context, problem).ConfigureAwait(false) ?? problem.ToResult();
        }

        await next().ConfigureAwait(false);
    }

    /// <summary>The page a refused form reads again, as its controller hands it for the operation the form was sent to, answered with the
    /// refusal's status and never cached; null where the sender reads JSON, the form was no form, or the controller hands none.</summary>
    private static async Task<IActionResult?> Page(ResultExecutingContext context, Problem refused)
    {
        var http = context.HttpContext;
        if (!http.Request.HasFormContentType || Answers.WantsJson(http.Request) || context.ActionDescriptor is not ControllerActionDescriptor action)
            return null;
        var operation = EntityOperation.Of(action.MethodInfo, action.ControllerTypeInfo);
        if (!operation.HandsRefusedPage || operation.Kind is EntityEndpointOperationKind.None) return null;

        // A gate refuses before the action's controller is made, so one is made here to hand the page, and released after it.
        var factory = context.Controller is null ? http.RequestServices.GetRequiredService<IControllerFactory>() : null;
        var made = factory is null ? null : new ControllerContext(context);
        var controller = context.Controller ?? factory!.CreateController(made!);
        try
        {
            if (controller is not IRefusedPage pages
                || await pages.RefusedPage(operation.Kind, refused, http.RequestAborted).ConfigureAwait(false) is not { } page)
                return null;
            http.Response.Headers.CacheControl = "no-store";
            return new ObjectResult(page) { StatusCode = refused.Status };
        }
        finally
        {
            if (factory is not null) await factory.ReleaseControllerAsync(made!, controller).ConfigureAwait(false);
        }
    }

    /// <summary>The refusal of a body that could not bind: a JSON body bound to the entity as the operation refuses it, and otherwise one past
    /// its route's limit as too large, or each field binding could not take, as the form's own reading named it.</summary>
    private static async Task<Problem?> Unbound(ResultExecutingContext context)
    {
        var http = context.HttpContext;
        var request = http.Request;
        // A create and a change take their members as one JSON object; a JSON Patch is a list of operations, and a delete takes no body.
        if (context.ActionDescriptor is ControllerActionDescriptor action && request.HasJsonContentType() && request.Body.CanSeek
            && EntityOperation.Of(action.MethodInfo, action.ControllerTypeInfo) is { Kind: EntityEndpointOperationKind.Upsert or EntityEndpointOperationKind.Patch } operation
            && !string.Equals(request.ContentType?.Split(';', 2)[0].Trim(), Infrastructure.KoanWebConstants.ContentTypes.ApplicationJsonPatch, StringComparison.OrdinalIgnoreCase))
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var raw = await reader.ReadToEndAsync(http.RequestAborted).ConfigureAwait(false);
            request.Body.Position = 0;
            JToken? body = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(raw)) body = JToken.Parse(raw);
            }
            catch (JsonException)
            {
                // Not JSON at all is not one object.
            }

            var serializer = JsonSerializer.Create(http.RequestServices.GetRequiredService<IOptions<MvcNewtonsoftJsonOptions>>().Value.SerializerSettings);
            if (operation.Unfit(body, serializer) is { } reason)
                return (Answers.Refused(http, reason) as ObjectResult)?.Value as Problem;
        }

        return (Answers.Unbound(http, context.ModelState) as ObjectResult)?.Value as Problem;
    }
}
