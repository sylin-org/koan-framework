using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace Sora.Web.Transformers;

internal sealed class EntityInputTransformFormatter : InputFormatter
{
    private readonly ITransformerRegistry _registry;
    public EntityInputTransformFormatter(ITransformerRegistry registry)
    {
        _registry = registry;
        // We claim all types; we’ll narrow at runtime per-entity
        SupportedMediaTypes.Clear();
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("*/*"));
    }

    protected override bool CanReadType(Type type)
    {
        // We only participate for IEntity<> targets; controller model types will be TEntity or IEnumerable<TEntity>
        if (type == typeof(string)) return false;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var item = type.GetGenericArguments()[0];
            return ImplementsIEntity(item);
        }
        return ImplementsIEntity(type);
    }

    private static bool ImplementsIEntity(Type t)
        => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Sora.Data.Abstractions.IEntity<>));

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        var http = context.HttpContext;
        var modelType = context.ModelType;
        bool isMany = false; Type entityType;
        if (modelType.IsGenericType && modelType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            isMany = true; entityType = modelType.GetGenericArguments()[0];
        }
        else entityType = modelType;

    var contentType = http.Request.ContentType ?? string.Empty;
        var resolver = typeof(ITransformerRegistry).GetMethod(nameof(ITransformerRegistry.ResolveForInput))!.MakeGenericMethod(entityType);
        var match = resolver.Invoke(_registry, new object?[] { contentType });
    // If no matching transformer, let the next formatter handle (likely JSON)
    if (match is null) return await InputFormatterResult.NoValueAsync();
        var transformer = match.GetType().GetProperty("Transformer")!.GetValue(match)!;

    // Don't dispose the request body; the framework owns it
    var body = http.Request.Body;
        if (isMany)
        {
            var mi = transformer.GetType().GetMethod("ParseManyAsync")!;
            var entities = await (Task<IReadOnlyList<object>>)mi.Invoke(transformer, new object?[] { body, contentType, http })!;
            return await InputFormatterResult.SuccessAsync(entities);
        }
        else
        {
            var mi = transformer.GetType().GetMethod("ParseAsync")!;
            var entity = await (Task<object>)mi.Invoke(transformer, new object?[] { body, contentType, http })!;
            return await InputFormatterResult.SuccessAsync(entity);
        }
    }
}
