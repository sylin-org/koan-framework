using Koan.Web.Controllers;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Hosting;

/// <summary>
/// WEB-0088 — the method a page's form asks for. A browser's form can only <c>POST</c>, so a form that changes or deletes a row names
/// <c>PATCH</c> or <c>DELETE</c> in its <see cref="KoanWebConstants.Form.Method"/> field, and the request is routed as that method. Only a
/// urlencoded form is read here, before routing, so a multipart upload reaches its endpoint and that endpoint's own limits unread; any
/// other method a field names changes nothing. It runs after the cross-site check (SEC-0039), which judged the request as the <c>POST</c>
/// it arrived as.
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
                if (HttpMethods.IsPatch(asked) || HttpMethods.IsDelete(asked))
                    request.Method = HttpMethods.GetCanonicalizedValue(asked);
            }
            catch (InvalidDataException)
            {
                // A form that cannot be read asks for nothing; its endpoint meets the same form and answers it.
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
