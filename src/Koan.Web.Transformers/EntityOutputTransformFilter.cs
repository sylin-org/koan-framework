using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Koan.Web.Transformers;

internal sealed class EntityOutputTransformFilter : IAsyncResultFilter
{
    private readonly ITransformerRegistry _registry;
    public EntityOutputTransformFilter(ITransformerRegistry registry) => _registry = registry;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Only apply to controllers marked with EnableEntityTransformersAttribute and 2xx results
        if (context.Controller.GetType().GetCustomAttributes(typeof(EnableEntityTransformersAttribute), inherit: true).Length == 0)
        { await next(); return; }
        if (context.Result is not ObjectResult or) { await next(); return; }
        if (or.StatusCode is >= 300) { await next(); return; }
        var entityType = or.Value?.GetType();
        if (entityType is null) { await next(); return; }

        // Handle IEnumerable<TEntity>
        bool isEnumerable = false;
        Type? itemType = null;
        if (entityType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(entityType))
        {
            var ienum = entityType.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            if (ienum is not null) { itemType = ienum.GetGenericArguments()[0]; isEnumerable = true; }
        }
        var targetType = isEnumerable ? itemType : entityType;
        if (targetType is null) { await next(); return; }

        // Resolve Accept header(s). If client didn't specify, don't transform.
        var accepts = context.HttpContext.Request.Headers["Accept"].ToString();
        if (string.IsNullOrWhiteSpace(accepts)) { await next(); return; }
        var acceptValues = accepts.Split(',').Select(s => s.Trim()).ToArray();

        var selection = _registry.ResolveForOutput(targetType, acceptValues);
        if (selection is null) { await next(); return; }

        object transformed;
        if (isEnumerable)
        {
            if (or.Value is not IEnumerable enumerable)
            {
                await next();
                return;
            }

            transformed = await selection.Invoker.TransformManyAsync(enumerable, context.HttpContext).ConfigureAwait(false);
        }
        else
        {
            transformed = await selection.Invoker.TransformAsync(or.Value!, context.HttpContext).ConfigureAwait(false);
        }
        // Set the negotiated content type so MVC doesn't re-serialize as JSON
        var result = new ObjectResult(transformed) { StatusCode = or.StatusCode };
        result.ContentTypes.Clear();
        result.ContentTypes.Add(selection.ContentType);
        context.Result = result;
        await next();
    }
}