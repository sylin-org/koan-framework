using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Koan.Web.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Koan.Web.OpenApi.Transformers;

/// <summary>
/// Adds Koan pagination metadata to OpenAPI operations when <see cref="PaginationAttribute"/> is present.
/// </summary>
internal sealed class PaginationOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var httpMethod = context.Description?.HttpMethod;
        if (!string.Equals(httpMethod, HttpMethods.Get, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var method = ResolveMethod(context);
        if (method is null)
        {
            return Task.CompletedTask;
        }

        var attr = ResolveAttribute(method);
        if (attr is null)
        {
            return Task.CompletedTask;
        }

        operation.Parameters ??= new List<IOpenApiParameter>();
        EnsureResponseShell(operation);
        AddSafeResponses(operation);

        switch (attr.Mode)
        {
            case PaginationMode.On:
            case PaginationMode.Required:
                AddPagingParameters(operation, attr);
                AddPagingHeaders(operation.Responses!, attr.IncludeCount);
                break;
            case PaginationMode.Optional:
                AddPagingParameters(operation, attr);
                AddOptionalParameters(operation);
                AddPagingHeaders(operation.Responses!, attr.IncludeCount);
                break;
            case PaginationMode.Off:
                if (attr.IncludeCount)
                {
                    AddCountHeaders(operation.Responses!);
                }
                break;
        }

        return Task.CompletedTask;
    }

    private static MethodInfo? ResolveMethod(OpenApiOperationTransformerContext context)
    {
        if (context.Description?.ActionDescriptor is ControllerActionDescriptor controllerAction)
        {
            return controllerAction.MethodInfo;
        }

        return context.Description?.ActionDescriptor?.EndpointMetadata?
            .OfType<MethodInfo>()
            .FirstOrDefault();
    }

    private static PaginationAttribute? ResolveAttribute(MethodInfo method)
    {
        if (method.GetCustomAttribute<PaginationAttribute>() is { } direct)
        {
            return direct;
        }

        return method.DeclaringType?.GetCustomAttribute<PaginationAttribute>();
    }

    private static void AddPagingParameters(OpenApiOperation operation, PaginationAttribute attr)
    {
        AddOrUpdateParameter(operation.Parameters!, "page", new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Minimum = "1",
            Default = JsonValue.Create(1)
        },
        "Page number (1-based). Default 1.");

        AddOrUpdateParameter(operation.Parameters!, "pageSize", new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Minimum = "1",
            Maximum = attr.MaxSize.ToString(CultureInfo.InvariantCulture),
            Default = JsonValue.Create(attr.DefaultSize)
        },
        $"Page size (max {attr.MaxSize}). Default {attr.DefaultSize}.");
    }

    private static void AddOptionalParameters(OpenApiOperation operation)
    {
        AddOrUpdateParameter(operation.Parameters!, "all", new OpenApiSchema
        {
            Type = JsonSchemaType.Boolean,
            Default = JsonValue.Create(false)
        },
        "When true, requests the full dataset (subject to safety caps).");
    }

    private static void AddPagingHeaders(OpenApiResponses responses, bool includeCount)
    {
        foreach (var responsePair in responses)
        {
            var statusCode = responsePair.Key;
            var response = responsePair.Value;

            if (!statusCode.StartsWith("2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (response is not OpenApiResponse mutableResponse)
            {
                continue;
            }

            AddHeaderIfMissing(mutableResponse, "X-Page", "Current page number.");
            AddHeaderIfMissing(mutableResponse, "X-Page-Size", "Effective page size.");

            if (includeCount)
            {
                AddHeaderIfMissing(mutableResponse, "X-Total-Count", "Total number of records matching the query.");
                AddHeaderIfMissing(mutableResponse, "X-Total-Pages", "Total number of pages available.");
            }
        }
    }

    private static void AddCountHeaders(OpenApiResponses responses)
    {
        foreach (var responsePair in responses)
        {
            var statusCode = responsePair.Key;
            var response = responsePair.Value;

            if (!statusCode.StartsWith("2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (response is not OpenApiResponse mutableResponse)
            {
                continue;
            }

            AddHeaderIfMissing(mutableResponse, "X-Total-Count", "Total number of records matching the query.");
        }
    }

    private static void EnsureResponseShell(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.Count == 0)
        {
            operation.Responses[StatusCodes.Status200OK.ToString()] = new OpenApiResponse { Description = "OK" };
        }
    }

    private static void AddSafeResponses(OpenApiOperation operation)
    {
        if (operation.Responses is null)
        {
            operation.Responses = new OpenApiResponses();
        }

        var status = StatusCodes.Status413PayloadTooLarge.ToString();
        if (operation.Responses.ContainsKey(status))
        {
            return;
        }

        operation.Responses[status] = new OpenApiResponse
        {
            Description = "Payload too large – enable pagination or refine filters.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase)
            {
                ["application/json"] = new OpenApiMediaType()
            }
        };
    }

    private static void AddOrUpdateParameter(IList<IOpenApiParameter> parameters, string name, OpenApiSchema schema, string description)
    {
        var existing = parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (existing is OpenApiParameter mutable)
            {
                mutable.Schema = schema;
                mutable.Description = description;
                mutable.In = ParameterLocation.Query;
            }
            else
            {
                parameters.Remove(existing);
                parameters.Add(new OpenApiParameter
                {
                    Name = name,
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = schema,
                    Description = description
                });
            }
            return;
        }

        parameters.Add(new OpenApiParameter
        {
            Name = name,
            In = ParameterLocation.Query,
            Required = false,
            Schema = schema,
            Description = description
        });
    }

    private static void AddHeaderIfMissing(OpenApiResponse response, string name, string description)
    {
        response.Headers ??= new Dictionary<string, IOpenApiHeader>(StringComparer.OrdinalIgnoreCase);

        if (response.Headers.ContainsKey(name))
        {
            return;
        }

        response.Headers[name] = new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        };
    }
}
