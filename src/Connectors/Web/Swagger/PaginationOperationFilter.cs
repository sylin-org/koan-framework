using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Web.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Koan.Web.Connector.Swagger;

/// <summary>
/// Emits pagination query parameters and headers for EntityController endpoints
/// that declare a <see cref="PaginationAttribute"/>.
/// </summary>
public sealed class PaginationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var httpMethod = context.ApiDescription?.HttpMethod;
        if (!string.Equals(httpMethod, "GET", StringComparison.OrdinalIgnoreCase))
        {
            // Pagination parameters only apply to collection reads.
            return;
        }

        var attr = ResolveAttribute(context.MethodInfo);
        if (attr is null)
        {
            return;
        }

        operation.Parameters ??= new List<OpenApiParameter>();
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
                // Even Off mode enforces the absolute cap and may emit counts when explicitly enabled.
                if (attr.IncludeCount)
                {
                    AddCountHeaders(operation.Responses!);
                }
                break;
        }
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
            Type = "integer",
            Minimum = 1,
            Default = new OpenApiInteger(1)
        },
        "Page number (1-based). Default 1.");

        AddOrUpdateParameter(operation.Parameters!, "pageSize", new OpenApiSchema
        {
            Type = "integer",
            Minimum = 1,
            Maximum = attr.MaxSize,
            Default = new OpenApiInteger(attr.DefaultSize)
        },
        $"Page size (max {attr.MaxSize}). Default {attr.DefaultSize}.");
    }

    private static void AddOptionalParameters(OpenApiOperation operation)
    {
        AddOrUpdateParameter(operation.Parameters!, "all", new OpenApiSchema
        {
            Type = "boolean",
            Default = new OpenApiBoolean(false)
        },
        "When true, requests the full dataset (subject to safety caps).");
    }

    private static void AddPagingHeaders(OpenApiResponses responses, bool includeCount)
    {
        foreach (var (statusCode, response) in responses)
        {
            if (!statusCode.StartsWith("2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.OrdinalIgnoreCase);
            AddHeaderIfMissing(response.Headers, "X-Page", "Current page number.");
            AddHeaderIfMissing(response.Headers, "X-Page-Size", "Effective page size.");

            if (includeCount)
            {
                AddHeaderIfMissing(response.Headers, "X-Total-Count", "Total number of records matching the query.");
                AddHeaderIfMissing(response.Headers, "X-Total-Pages", "Total number of pages available.");
            }
        }
    }

    private static void AddCountHeaders(OpenApiResponses responses)
    {
        foreach (var (statusCode, response) in responses)
        {
            if (!statusCode.StartsWith("2", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            response.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.OrdinalIgnoreCase);
            AddHeaderIfMissing(response.Headers, "X-Total-Count", "Total number of records matching the query.");
        }
    }

    private static void EnsureResponseShell(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.Count == 0)
        {
            operation.Responses["200"] = new OpenApiResponse { Description = "OK" };
        }
    }

    private static void AddSafeResponses(OpenApiOperation operation)
    {
        if (!operation.Responses.ContainsKey(StatusCodes.Status413PayloadTooLarge.ToString()))
        {
            var payloadTooLarge = new OpenApiResponse
            {
                Description = "Payload too large – enable pagination or refine filters.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType()
                }
            };
            operation.Responses[StatusCodes.Status413PayloadTooLarge.ToString()] = payloadTooLarge;
        }
    }

    private static void AddOrUpdateParameter(IList<OpenApiParameter> parameters, string name, OpenApiSchema schema, string description)
    {
        var existing = parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Schema = schema;
            existing.Description = description;
            existing.In = ParameterLocation.Query;
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

    private static void AddHeaderIfMissing(IDictionary<string, OpenApiHeader> headers, string name, string description)
    {
        if (headers.ContainsKey(name))
        {
            return;
        }

        headers[name] = new OpenApiHeader
        {
            Description = description,
            Schema = new OpenApiSchema { Type = "string" }
        };
    }
}

