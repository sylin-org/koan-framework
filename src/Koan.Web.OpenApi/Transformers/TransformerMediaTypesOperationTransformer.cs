using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Koan.Web.OpenApi.Transformers;

/// <summary>
/// Mirrors Koan transformer media type registrations in OpenAPI when entity transformers are enabled.
/// </summary>
internal sealed class TransformerMediaTypesOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var attrType = Type.GetType("Koan.Web.Transformers.EnableEntityTransformersAttribute, Koan.Web.Transformers");
        var registryType = Type.GetType("Koan.Web.Transformers.ITransformerRegistry, Koan.Web.Transformers");
        if (attrType is null || registryType is null)
        {
            return Task.CompletedTask;
        }

        var controllerMethod = ResolveMethod(context);
        if (controllerMethod is null)
        {
            return Task.CompletedTask;
        }

        var hasAttribute = controllerMethod.DeclaringType?.GetCustomAttributes(attrType, inherit: true).Any() == true;
        if (!hasAttribute)
        {
            return Task.CompletedTask;
        }

        var registry = context.ApplicationServices?.GetService(registryType);
        if (registry is null)
        {
            return Task.CompletedTask;
        }

        TryAugmentResponseTypes(operation, controllerMethod.ReturnType, registryType, registry);

        foreach (var parameter in controllerMethod.GetParameters())
        {
            TryAugmentRequestTypes(operation, parameter.ParameterType, registryType, registry);
        }

        return Task.CompletedTask;
    }

    private static MethodInfo? ResolveMethod(OpenApiOperationTransformerContext context)
    {
        var actionDescriptor = context.Description?.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor;
        return actionDescriptor?.MethodInfo;
    }

    private static void TryAugmentResponseTypes(OpenApiOperation operation, Type returnType, Type registryType, object registry)
    {
        var entityType = TryResolveEntityType(returnType);
        if (entityType is null)
        {
            return;
        }

        var getMethod = registryType.GetMethod("GetContentTypes")?.MakeGenericMethod(entityType);
        if (getMethod is null)
        {
            return;
        }

        if (getMethod.Invoke(registry, Array.Empty<object>()) is IReadOnlyList<string> list && list.Count > 0)
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

    private static void TryAugmentRequestTypes(OpenApiOperation operation, Type parameterType, Type registryType, object registry)
    {
        var entityType = TryResolveEntityType(parameterType);
        if (entityType is null)
        {
            return;
        }

        var getMethod = registryType.GetMethod("GetContentTypes")?.MakeGenericMethod(entityType);
        if (getMethod is null)
        {
            return;
        }

        if (getMethod.Invoke(registry, Array.Empty<object>()) is IReadOnlyList<string> list && list.Count > 0)
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
