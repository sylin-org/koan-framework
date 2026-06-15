using System;
using System.Collections;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Koan.Web.Transformers;

internal sealed class EntityOutputTransformFilter : IAsyncResultFilter
{
    private readonly ITransformerRegistry _registry;

    public EntityOutputTransformFilter(ITransformerRegistry registry) => _registry = registry;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        // Controller-level opt-in: implementing ITransformerActivationPredicate enables the
        // pipeline for the controller. Returning false skips the pipeline for the current request
        // (e.g. ?raw=1, debugging surfaces, internal call paths).
        if (context.Controller is not ITransformerActivationPredicate controllerGate
            || !controllerGate.ShouldActivate(context.HttpContext))
        {
            await next();
            return;
        }

        if (context.Result is not ObjectResult or)
        {
            await next();
            return;
        }
        if (or.StatusCode is >= 300)
        {
            await next();
            return;
        }
        var valueType = or.Value?.GetType();
        if (valueType is null)
        {
            await next();
            return;
        }

        var (itemType, isCollection) = ResolveItemType(valueType);
        if (itemType is null)
        {
            await next();
            return;
        }

        var accepts = context.HttpContext.Request.Headers["Accept"].ToString();
        var acceptValues = string.IsNullOrWhiteSpace(accepts)
            ? System.Array.Empty<string>()
            : accepts.Split(',').Select(s => s.Trim()).ToArray();

        var selection = _registry.ResolveOutput(itemType, acceptValues, context.HttpContext);
        if (!selection.HasAny)
        {
            await next();
            return;
        }

        // Pipeline stage: apply each activated enricher in order. Each enricher receives the
        // (possibly already enriched) value from the previous step.
        var current = or.Value!;
        foreach (var enricher in selection.Pipeline)
        {
            current = isCollection
                ? await enricher.Invoker.EnrichMany((IEnumerable)current, context.HttpContext)
                : await enricher.Invoker.Enrich(current, context.HttpContext);
        }

        if (selection.Terminal is { } terminal)
        {
            // Terminal stage: shape-changing. Set the negotiated content type so MVC doesn't fall
            // back to JSON.
            var terminalOutput = isCollection
                ? await terminal.Invoker.TransformMany((IEnumerable)current, context.HttpContext)
                : await terminal.Invoker.Transform(current, context.HttpContext);

            var terminalResult = new ObjectResult(terminalOutput) { StatusCode = or.StatusCode };
            terminalResult.ContentTypes.Clear();
            terminalResult.ContentTypes.Add(terminal.ContentType);
            context.Result = terminalResult;
        }
        else if (!ReferenceEquals(current, or.Value))
        {
            // Pipeline-only: forward the enriched value through MVC's default JSON serializer.
            // We don't replace the result when nothing changed — keeps headers and content-type
            // negotiation identical to the no-enricher path.
            context.Result = new ObjectResult(current) { StatusCode = or.StatusCode };
        }

        await next();
    }

    private static (Type? ItemType, bool IsCollection) ResolveItemType(Type valueType)
    {
        if (valueType == typeof(string))
        {
            return (null, false);
        }
        if (!typeof(IEnumerable).IsAssignableFrom(valueType))
        {
            return (valueType, false);
        }

        var ienum = valueType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return ienum is null ? (null, false) : (ienum.GetGenericArguments()[0], true);
    }
}
