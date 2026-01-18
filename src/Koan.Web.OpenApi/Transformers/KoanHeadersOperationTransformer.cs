using System;
using System.Collections.Generic;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Koan.Web.OpenApi.Transformers;

/// <summary>
/// Ensures Koan correlation headers are documented on responses.
/// </summary>
internal sealed class KoanHeadersOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.Count == 0)
        {
            operation.Responses[StatusCodes.Status200OK.ToString()] = new OpenApiResponse { Description = "OK" };
        }

        foreach (var responsePair in operation.Responses)
        {
            if (responsePair.Value is not OpenApiResponse response)
            {
                continue;
            }

            EnsureHeader(response, KoanWebConstants.Headers.KoanTraceId, "Trace correlation id for this response.");

            if (string.Equals(context.Description?.HttpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
            {
                EnsureHeader(response, "Koan-InMemory-Paging", "true when in-memory pagination fallback occurred.", JsonSchemaType.Boolean);
            }
        }

        return Task.CompletedTask;
    }

    private static void EnsureHeader(OpenApiResponse response, string name, string description, JsonSchemaType schemaType = JsonSchemaType.String)
    {
        response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);

        if (response.Headers.ContainsKey(name))
        {
            return;
        }

        response.Headers[name] = new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = schemaType }
        };
    }
}
