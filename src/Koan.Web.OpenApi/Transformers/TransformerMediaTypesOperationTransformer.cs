using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Koan.Web.Transformers;

namespace Koan.Web.OpenApi.Transformers;

/// <summary>
/// Mirrors Koan Terminal-stage transformer media-type registrations in OpenAPI. Pipeline-stage
/// enrichers (<c>IEntityEnricher&lt;T&gt;</c>) do not change the wire shape and are deliberately
/// excluded — they are not Accept-selectable variants.
/// </summary>
internal sealed class TransformerMediaTypesOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        // Controllers opt into the transformer pipeline by implementing ITransformerActivationPredicate
        // (WEB-0067). Koan.Web.OpenApi references Koan.Web (which now owns the transformers), so we use
        // the public predicate + registry types directly.
        var controllerMethod = ResolveMethod(context);
        if (controllerMethod is null)
        {
            return Task.CompletedTask;
        }

        var declaringType = controllerMethod.DeclaringType;
        var optsIn = declaringType is not null && typeof(ITransformerActivationPredicate).IsAssignableFrom(declaringType);
        if (!optsIn)
        {
            return Task.CompletedTask;
        }

        var registry = context.ApplicationServices?.GetService<ITransformerRegistry>();
        if (registry is null)
        {
            return Task.CompletedTask;
        }

        TryAugmentResponseTypes(operation, controllerMethod.ReturnType, registry);

        foreach (var parameter in controllerMethod.GetParameters())
        {
            TryAugmentRequestTypes(operation, parameter.ParameterType, registry);
        }

        return Task.CompletedTask;
    }

    private static MethodInfo? ResolveMethod(OpenApiOperationTransformerContext context)
    {
        var actionDescriptor = context.Description?.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor;
        return actionDescriptor?.MethodInfo;
    }

    private static void TryAugmentResponseTypes(OpenApiOperation operation, Type returnType, ITransformerRegistry registry)
    {
        var entityType = TryResolveEntityType(returnType);
        if (entityType is null)
        {
            return;
        }

        if (registry.GetContentTypes(entityType) is { Count: > 0 } list)
        {
            var responses = EnsureResponses(operation);
            if (!responses.TryGetValue("200", out var okResponse) || okResponse is not OpenApiResponse mutableResponse)
            {
                mutableResponse = new OpenApiResponse { Description = "OK" };
                responses["200"] = mutableResponse;
            }

            mutableResponse.Content ??= new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase);
            foreach (var contentType in list)
            {
                if (!mutableResponse.Content.ContainsKey(contentType))
                {
                    mutableResponse.Content[contentType] = new OpenApiMediaType();
                }
            }
        }
    }

    private static void TryAugmentRequestTypes(OpenApiOperation operation, Type parameterType, ITransformerRegistry registry)
    {
        var entityType = TryResolveEntityType(parameterType);
        if (entityType is null)
        {
            return;
        }

        if (registry.GetContentTypes(entityType) is { Count: > 0 } list)
        {
            operation.RequestBody ??= new OpenApiRequestBody { Required = true };
            if (operation.RequestBody is OpenApiRequestBody requestBody)
            {
                requestBody.Content ??= new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase);
                foreach (var contentType in list)
                {
                    if (!requestBody.Content.ContainsKey(contentType))
                    {
                        requestBody.Content[contentType] = new OpenApiMediaType();
                    }
                }
            }
        }
    }

    private static OpenApiResponses EnsureResponses(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();
        return (OpenApiResponses)operation.Responses;
    }

    private static Type? TryResolveEntityType(Type type)
    {
        if (type == typeof(void) || type == typeof(string))
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Microsoft.AspNetCore.Mvc.ActionResult<>))
        {
            type = type.GetGenericArguments()[0];
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            type = type.GetGenericArguments()[0];
        }

        return type.GetInterfaces().Any(i => i.IsGenericType && string.Equals(i.GetGenericTypeDefinition().FullName, "Koan.Data.Abstractions.IEntity`1", StringComparison.Ordinal))
            ? type
            : null;
    }
}
