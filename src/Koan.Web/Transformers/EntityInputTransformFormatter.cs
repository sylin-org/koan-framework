using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace Koan.Web.Transformers;

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

    public override bool CanRead(InputFormatterContext context)
    {
        // Two-stage check. CanReadType keeps us out of non-entity targets (cheap reflection). The
        // runtime check below consults the registry so we only claim the body when a Terminal
        // transformer is actually registered for this (entity, content-type) pair. Without this,
        // assemblies that pull in Koan.Web.Transformers solely for output enrichers (no input
        // transformers) would still see this formatter claim every `IEntity<>` POST, ReadRequestBody
        // would return NoValue, and `[FromBody]` would bind null — breaking standard JSON upserts.
        if (!base.CanRead(context)) return false;
        var modelType = context.ModelType;
        var entityType = modelType.IsGenericType && modelType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? modelType.GetGenericArguments()[0]
            : modelType;
        var contentType = context.HttpContext.Request.ContentType ?? string.Empty;
        return _registry.ResolveForInput(entityType, contentType, context.HttpContext) is not null;
    }

    private static bool ImplementsIEntity(Type type)
    {
        var current = type;
        while (current is not null && current != typeof(object))
        {
            if (current.IsGenericType)
            {
                var definition = current.GetGenericTypeDefinition();
                if (definition == typeof(Koan.Data.Core.Model.Entity<>) || definition == typeof(Koan.Data.Core.Model.Entity<,>))
                {
                    return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

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

        var contentType = http.Request.ContentType ?? "";
        var selection = _registry.ResolveForInput(entityType, contentType, http);
        if (selection is null)
        {
            return InputFormatterResult.NoValue();
        }

        // Don't dispose the request body; the framework owns it
        var body = http.Request.Body;
        if (isMany)
        {
            var entities = await selection.Invoker.ParseMany(body, contentType, http);
            return InputFormatterResult.Success(entities);
        }
        else
        {
            var entity = await selection.Invoker.Parse(body, contentType, http);
            return InputFormatterResult.Success(entity);
        }
    }
}
