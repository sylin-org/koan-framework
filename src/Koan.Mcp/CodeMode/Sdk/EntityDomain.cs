using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Mcp.Execution;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Jint.Runtime;

namespace Koan.Mcp.CodeMode.Sdk;

/// <summary>
/// SDK.Entities.* - Dynamic access to entity operations.
/// Creates proxies for each entity on-demand.
/// </summary>
public sealed class EntityDomain
{
    private readonly IServiceProvider _services;
    private readonly ConcurrentDictionary<string, object> _entityProxies = new();

    public EntityDomain(IServiceProvider services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    /// <summary>
    /// JavaScript accesses SDK.Entities.Todo via property indexer.
    /// Returns a proxy object with operation methods.
    /// </summary>
    public object this[string entityName]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(entityName))
            {
                throw new JavaScriptException($"Entity name cannot be empty");
            }

            return _entityProxies.GetOrAdd(entityName, CreateEntityProxy);
        }
    }

    private object CreateEntityProxy(string entityName)
    {
        var registry = _services.GetRequiredService<McpEntityRegistry>();

        if (!registry.TryGetRegistration(entityName, out var registration))
        {
            throw new JavaScriptException($"Entity '{entityName}' not found in MCP registry");
        }

        return new EntityOperationsProxy(registration, _services);
    }
}

/// <summary>
/// Proxy for a specific entity's operations.
/// Exposes: collection, getById, upsert, delete, deleteMany.
/// All methods are synchronous from JavaScript perspective but call async C# methods.
/// </summary>
internal sealed class EntityOperationsProxy
{
    private readonly McpEntityRegistration _registration;
    private readonly IServiceProvider _services;
    private readonly EndpointToolExecutor _executor;
    private readonly MetricsDomain _metrics;

    public EntityOperationsProxy(McpEntityRegistration registration, IServiceProvider services)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _executor = services.GetRequiredService<EndpointToolExecutor>();

        // Get metrics domain from SDK bindings if available
        var bindings = services.GetService<KoanSdkBindings>();
        _metrics = bindings?.Metrics ?? new MetricsDomain();
    }

    /// <summary>
    /// SDK.Entities.Todo.collection({ filter, pageSize, set, with })
    /// Returns: { items: [...], page: 1, pageSize: 10, totalCount: 42 }
    /// </summary>
    public object collection(object? args = null)
    {
        _metrics.IncrementCalls();

        var tool = FindTool(EntityEndpointOperationKind.Collection);
        var argsJson = ConvertToJsonObject(args);

        // Synchronously execute async operation (Jint handles this)
        var result = _executor.ExecuteAsync(tool.Name, argsJson, default).GetAwaiter().GetResult();

        return ConvertToJavaScriptObject(result);
    }

    /// <summary>
    /// SDK.Entities.Todo.getById(id, options)
    /// Options: { set?: string, with?: string }
    /// Returns: { id: "...", title: "...", ... }
    /// </summary>
    public object getById(string id, object? options = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JavaScriptException("Entity ID cannot be empty");
        }

        _metrics.IncrementCalls();

        var tool = FindTool(EntityEndpointOperationKind.GetById);
        var args = new JsonObject { ["id"] = id };

        // Extract options if provided
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson != null)
            {
                if (optsJson.TryGetPropertyValue("set", out var setNode))
                    args["set"] = setNode?.DeepClone();

                if (optsJson.TryGetPropertyValue("with", out var withNode))
                    args["with"] = withNode?.DeepClone();
            }
        }

        var result = _executor.ExecuteAsync(tool.Name, args, default).GetAwaiter().GetResult();

        return ConvertToJavaScriptObject(result);
    }

    /// <summary>
    /// SDK.Entities.Todo.upsert(model, options)
    /// Options: { set?: string }
    /// Returns: { id: "...", title: "...", ... }
    /// </summary>
    public object upsert(object model, object? options = null)
    {
        if (model == null)
        {
            throw new JavaScriptException("Model cannot be null");
        }

        _metrics.IncrementCalls();

        var tool = FindTool(EntityEndpointOperationKind.Upsert);
        var args = new JsonObject
        {
            ["model"] = ConvertToJsonNode(model)
        };

        // Extract options
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson?.TryGetPropertyValue("set", out var setNode) == true)
            {
                args["set"] = setNode?.DeepClone();
            }
        }

        var result = _executor.ExecuteAsync(tool.Name, args, default).GetAwaiter().GetResult();

        return ConvertToJavaScriptObject(result);
    }

    /// <summary>
    /// SDK.Entities.Todo.delete(id, options)
    /// Options: { set?: string }
    /// Returns: number (count deleted, typically 1)
    /// </summary>
    public int delete(string id, object? options = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JavaScriptException("Entity ID cannot be empty");
        }

        _metrics.IncrementCalls();

        var tool = FindTool(EntityEndpointOperationKind.Delete);
        var args = new JsonObject { ["id"] = id };

        // Extract options
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson?.TryGetPropertyValue("set", out var setNode) == true)
            {
                args["set"] = setNode?.DeepClone();
            }
        }

        _executor.ExecuteAsync(tool.Name, args, default).GetAwaiter().GetResult();

        return 1; // Successfully deleted
    }

    /// <summary>
    /// SDK.Entities.Todo.deleteMany(ids, options)
    /// Options: { set?: string }
    /// Returns: number (count deleted)
    /// </summary>
    public int deleteMany(object idsArg, object? options = null)
    {
        if (idsArg == null)
        {
            throw new JavaScriptException("IDs array cannot be null");
        }

        _metrics.IncrementCalls();

        var tool = FindTool(EntityEndpointOperationKind.DeleteMany);

        // Convert ids to JSON array
        JsonArray idsArray;
        if (idsArg is IEnumerable<object> enumerable)
        {
            idsArray = new JsonArray(enumerable.Select(id => (JsonNode?)JsonValue.Create(id?.ToString())).ToArray());
        }
        else
        {
            var idsJson = ConvertToJsonNode(idsArg);
            if (idsJson is not JsonArray arr)
            {
                throw new JavaScriptException("IDs must be an array");
            }
            idsArray = arr;
        }

        var args = new JsonObject { ["ids"] = idsArray };

        // Extract options
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson?.TryGetPropertyValue("set", out var setNode) == true)
            {
                args["set"] = setNode?.DeepClone();
            }
        }

        var result = _executor.ExecuteAsync(tool.Name, args, default).GetAwaiter().GetResult();

        // Extract count from result
        return ExtractCount(result);
    }

    private McpToolDefinition FindTool(EntityEndpointOperationKind kind)
    {
        var tool = _registration.Tools.FirstOrDefault(t => t.Operation == kind);

        if (tool == null)
        {
            // Check if it's a mutation operation that's disallowed
            var isMutation = kind is EntityEndpointOperationKind.Upsert
                or EntityEndpointOperationKind.Delete
                or EntityEndpointOperationKind.DeleteMany
                or EntityEndpointOperationKind.Patch;

            var operationName = kind.ToString();

            throw new JavaScriptException(
                isMutation
                    ? $"Mutation operation '{operationName}' is not allowed for entity '{_registration.DisplayName}' (AllowMutations = false)"
                    : $"Operation '{operationName}' is not available for entity '{_registration.DisplayName}'");
        }

        return tool;
    }

    private JsonObject? ConvertToJsonObject(object? obj)
    {
        if (obj == null) return null;

        try
        {
            var json = JsonSerializer.Serialize(obj);
            return JsonNode.Parse(json)?.AsObject();
        }
        catch (Exception ex)
        {
            throw new JavaScriptException($"Failed to convert argument to JSON: {ex.Message}");
        }
    }

    private JsonNode? ConvertToJsonNode(object? obj)
    {
        if (obj == null) return null;

        try
        {
            var json = JsonSerializer.Serialize(obj);
            return JsonNode.Parse(json);
        }
        catch (Exception ex)
        {
            throw new JavaScriptException($"Failed to convert object to JSON: {ex.Message}");
        }
    }

    private object ConvertToJavaScriptObject(McpToolExecutionResult result)
    {
        if (!result.Success)
        {
            var errorMsg = !string.IsNullOrWhiteSpace(result.ErrorCode)
                ? $"{result.ErrorCode}: {result.ErrorMessage}"
                : result.ErrorMessage ?? "Operation failed";

            throw new JavaScriptException(errorMsg);
        }

        if (result.Payload == null)
        {
            return new { }; // Empty object
        }

        try
        {
            // Convert JSON payload to plain object for JavaScript
            var json = result.Payload.ToJsonString();
            return JsonSerializer.Deserialize<object>(json) ?? new { };
        }
        catch (Exception ex)
        {
            throw new JavaScriptException($"Failed to convert result to JavaScript object: {ex.Message}");
        }
    }

    private int ExtractCount(McpToolExecutionResult result)
    {
        if (!result.Success)
        {
            return 0;
        }

        if (result.Payload is JsonObject obj && obj.TryGetPropertyValue("count", out var countNode))
        {
            if (countNode is JsonValue val && val.TryGetValue<long>(out var count))
            {
                return (int)count;
            }
        }

        // Fallback: assume 1 if we got here successfully
        return 1;
    }
}
