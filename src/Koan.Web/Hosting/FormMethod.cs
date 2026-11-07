using Koan.Web.Controllers;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0088 — the method a page's form asks for. A browser's form can only <c>POST</c>, so a form that changes or deletes a row names
/// <c>PATCH</c> or <c>DELETE</c> in its <see cref="KoanWebConstants.Form.Method"/> field, and the request is routed as that method where a
/// declared <see cref="EntityController{TEntity,TKey}"/>'s own change or delete takes that method at the address it was sent to; anywhere
/// else, an undeclared controller's address among them, it stays the <c>POST</c> it arrived as. Only a urlencoded form is read here,
/// before routing, so a multipart upload reaches its endpoint and that endpoint's own limits unread; any other method a field names
/// changes nothing. It runs after the cross-site check (SEC-0039), which judged the request as the <c>POST</c> it arrived as.
/// </summary>
internal static class FormMethod
{
    /// <summary>Whether this host serves a form write: some controller it maps is a declared <see cref="EntityController{TEntity,TKey}"/>.</summary>
    public static bool Served(IServiceProvider services)
    {
        if (services.GetService<ApplicationPartManager>() is not { } parts) return false;
        var controllers = new ControllerFeature();
        parts.PopulateFeature(controllers);
        return controllers.Controllers.Any(controller => EntityOperation.IsDeclared(controller) && EntityOperation.EntityOf(controller) is not null);
    }

    public static async Task Override(HttpContext context, RequestDelegate next)
    {
        var request = context.Request;
        if (HttpMethods.IsPost(request.Method) && MediaTypeHeaderValue.TryParse(request.ContentType, out var media)
            && media.MediaType.Equals(KoanWebConstants.ContentTypes.FormUrlEncoded, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var asked = (await request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false))[KoanWebConstants.Form.Method].ToString();
                if ((HttpMethods.IsPatch(asked) || HttpMethods.IsDelete(asked)) && HttpMethods.GetCanonicalizedValue(asked) is var method && Takes(context, method))
                    request.Method = method;
            }
            catch (InvalidDataException)
            {
                // A form that cannot be read asks for nothing; its endpoint meets the same form and answers it.
            }
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>Whether a declared controller's own change or delete takes <paramref name="method"/> at the address the request was sent to.</summary>
    private static bool Takes(HttpContext context, string method)
    {
        var path = context.Request.Path;
        foreach (var endpoint in context.RequestServices.GetRequiredService<EndpointDataSource>().Endpoints)
        {
            if (endpoint is not RouteEndpoint route
                || endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not { } action
                || !EntityOperation.IsDeclared(action.ControllerTypeInfo) || !EntityOperation.IsEntityOperation(action.MethodInfo)
                || endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase) != true)
                continue;
            if (new TemplateMatcher(new RouteTemplate(route.RoutePattern), new RouteValueDictionary(route.RoutePattern.Defaults)).TryMatch(path, new RouteValueDictionary()))
                return true;
        }
        return false;
    }
}
