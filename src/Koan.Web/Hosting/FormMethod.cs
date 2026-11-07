using Koan.Web.Controllers;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0088 — the method a page's form asks for. A browser's form can only <c>POST</c>, so a form that changes or deletes a row names
/// <c>PATCH</c> or <c>DELETE</c> in its <see cref="KoanWebConstants.Form.Method"/> field, and the request is routed as that method where a
/// declared <see cref="EntityController{TEntity,TKey}"/>'s own change or delete takes that method at the address it was sent to; anywhere
/// else, an undeclared controller's address among them, it stays the <c>POST</c> it arrived as. A urlencoded form is read here, before
/// routing, only where it was sent to such a change or delete, and then within the strictest body and form limits of every endpoint it may
/// reach there, so a form past them answers <c>413</c> as its route would; every other request, a multipart upload among them, reaches its
/// endpoint and that endpoint's own limits unread. Any other method a field names changes nothing. It runs after the cross-site check
/// (SEC-0039), which judged the request as the <c>POST</c> it arrived as.
/// </summary>
internal sealed class FormMethod
{
    /// <summary>The endpoints a form may reach, compiled once for the endpoints the host maps.</summary>
    private Compiled? _compiled;

    /// <summary>Whether this host serves a form write: some controller it maps is a declared <see cref="EntityController{TEntity,TKey}"/>.</summary>
    public static bool Served(IServiceProvider services)
    {
        if (services.GetService<ApplicationPartManager>() is not { } parts) return false;
        var controllers = new ControllerFeature();
        parts.PopulateFeature(controllers);
        return controllers.Controllers.Any(controller => EntityOperation.IsDeclared(controller) && EntityOperation.EntityOf(controller) is not null);
    }

    public async Task Override(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (HttpMethods.IsPost(request.Method) && MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            && media.MediaType.Equals(KoanWebConstants.ContentTypes.FormUrlEncoded, StringComparison.OrdinalIgnoreCase)
            && Reached(context) is { } reached)
        {
            Bound(context, reached);
            try
            {
                var asked = (await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false))[KoanWebConstants.Form.Method].ToString();
                if ((HttpMethods.IsPatch(asked) || HttpMethods.IsDelete(asked)) && HttpMethods.GetCanonicalizedValue(asked) is var method
                    && reached.Any(endpoint => endpoint.Changes && endpoint.Methods.Contains(method, StringComparer.OrdinalIgnoreCase)))
                    request.Method = method;
            }
            catch (BadHttpRequestException tooLarge) when (tooLarge.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                await Answers.TooLarge().ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor())).ConfigureAwait(false);
                return;
            }
            catch (InvalidDataException)
            {
                // A form that cannot be read asks for nothing; its endpoint meets the same form and answers it.
            }
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>Every endpoint a urlencoded <c>POST</c> to this address may reach, where a declared change or delete is among them; otherwise
    /// null, and the request is left as it came.</summary>
    private List<Reach>? Reached(HttpContext context)
    {
        var path = context.Request.Path;
        List<Reach>? reached = null;
        var changes = false;
        foreach (var endpoint in Plan(context))
        {
            if (!endpoint.Matcher.TryMatch(path, new RouteValueDictionary())) continue;
            (reached ??= []).Add(endpoint);
            changes |= endpoint.Changes;
        }
        return changes ? reached : null;
    }

    /// <summary>The body and form limits the form is read within: of each endpoint it may reach, the strictest, so none is read past its own.</summary>
    private static void Bound(HttpContext context, List<Reach> reached)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            var server = body.MaxRequestBodySize;
            long? least = null;
            for (var i = 0; i < reached.Count; i++)
            {
                // An endpoint naming no limit takes the server's; one that disables its limit takes any size.
                var own = reached[i].Endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>() is { } limit ? limit.MaxRequestBodySize : server;
                least = i == 0 ? own : least is null ? own : own is null ? least : Math.Min(least.Value, own.Value);
            }
            body.MaxRequestBodySize = least;
        }

        var forms = reached.Select(endpoint => endpoint.Endpoint.Metadata.GetMetadata<IFormOptionsMetadata>()).OfType<IFormOptionsMetadata>().ToArray();
        if (forms.Length == 0 || context.Features.Get<IFormFeature>()?.Form is not null) return;
        var host = context.RequestServices.GetService<IOptions<FormOptions>>()?.Value ?? new FormOptions();
        context.Features.Set<IFormFeature>(new FormFeature(context.Request, new FormOptions
        {
            BufferBody = host.BufferBody,
            MemoryBufferThreshold = host.MemoryBufferThreshold,
            BufferBodyLengthLimit = host.BufferBodyLengthLimit,
            ValueCountLimit = forms.Select(form => form.ValueCountLimit).Aggregate(host.ValueCountLimit, Least),
            KeyLengthLimit = forms.Select(form => form.KeyLengthLimit).Aggregate(host.KeyLengthLimit, Least),
            ValueLengthLimit = forms.Select(form => form.ValueLengthLimit).Aggregate(host.ValueLengthLimit, Least),
        }));
    }

    private static int Least(int limit, int? own) => own is { } stated ? Math.Min(limit, stated) : limit;

    /// <summary>The endpoints a urlencoded <c>POST</c> may reach, compiled once for the endpoints the host maps, and again only where they change.</summary>
    private Reach[] Plan(HttpContext context)
    {
        var endpoints = context.RequestServices.GetRequiredService<EndpointDataSource>().Endpoints;
        if (_compiled is { } compiled && ReferenceEquals(compiled.From, endpoints)) return compiled.Reaches;
        var reaches = new List<Reach>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint is not RouteEndpoint route) continue;
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var changes = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is { } action
                          && EntityOperation.IsDeclared(action.ControllerTypeInfo) && EntityOperation.IsEntityOperation(action.MethodInfo)
                          && methods?.Any(method => HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method)) == true;
            // What a form that names no change reaches: an endpoint taking a POST, or any method.
            if (!changes && methods is not null && !methods.Any(HttpMethods.IsPost)) continue;
            reaches.Add(new Reach(new TemplateMatcher(new RouteTemplate(route.RoutePattern), new RouteValueDictionary(route.RoutePattern.Defaults)),
                endpoint, changes, methods ?? []));
        }
        var built = reaches.ToArray();
        _compiled = new Compiled(endpoints, built);
        return built;
    }

    /// <summary>An endpoint a form may reach: whether it is a declared change or delete, and the methods it takes.</summary>
    private sealed record Reach(TemplateMatcher Matcher, Endpoint Endpoint, bool Changes, IReadOnlyList<string> Methods);

    private sealed record Compiled(IReadOnlyList<Endpoint> From, Reach[] Reaches);
}
