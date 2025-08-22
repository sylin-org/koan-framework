using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Sora.Web.Transformers;

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

        var method = typeof(ITransformerRegistry).GetMethod(nameof(ITransformerRegistry.ResolveForOutput))!.MakeGenericMethod(targetType);
        var match = method.Invoke(_registry, new object?[] { acceptValues });
        if (match is null) { await next(); return; }

        // Invoke transformer
        var contentTypeProp = match.GetType().GetProperty("ContentType")!;
        var transformerObj = match.GetType().GetProperty("Transformer")!.GetValue(match)!;
        var contentType = (string)contentTypeProp.GetValue(match)!;

        object transformed;
        if (isEnumerable)
        {
            var mi = transformerObj.GetType().GetMethod("TransformManyAsync")!;
            transformed = await (Task<object>)mi.Invoke(transformerObj, new object?[] { or.Value, context.HttpContext })!;
        }
        else
        {
            var mi = transformerObj.GetType().GetMethod("TransformAsync")!;
            transformed = await (Task<object>)mi.Invoke(transformerObj, new object?[] { or.Value, context.HttpContext })!;
        }
        // Set the negotiated content type so MVC doesn't re-serialize as JSON
        var result = new ObjectResult(transformed) { StatusCode = or.StatusCode };
        result.ContentTypes.Clear();
        result.ContentTypes.Add(contentType);
        context.Result = result;
        await next();
    }
}