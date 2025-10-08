using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Dynamic;
using Koan.Mcp.CodeMode.Json;
using Newtonsoft.Json.Linq;
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
    private readonly MetricsDomain _metrics;
    private readonly ConcurrentDictionary<string, object> _entityProxies = new();

    private readonly IJsonFacade _json;

    public EntityDomain(IServiceProvider services, MetricsDomain metrics, IJsonFacade json)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _json = json ?? throw new ArgumentNullException(nameof(json));
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

    return new EntityOperationsProxy(registration, _services, _metrics, _json);
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
    private readonly IJsonFacade _json;

    public EntityOperationsProxy(McpEntityRegistration registration, IServiceProvider services, MetricsDomain metrics, IJsonFacade json)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _executor = services.GetRequiredService<EndpointToolExecutor>();
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _json = json ?? throw new ArgumentNullException(nameof(json));
    }

    /// <summary>
    /// SDK.Entities.Todo.collection({ filter, page, pageSize, set, with, sort })
    /// Returns: { items: [...], page: 1, pageSize: 10, totalCount: 42 }
    /// </summary>
    public object collection(object? args = null)
        => ExecuteCollection(args);

    /// <summary>
    /// SDK.Entities.Todo.firstPage(pageSize?, args?) - convenience for page 1.
    /// </summary>
    public object firstPage(int? pageSize = null, object? args = null)
    {
        // Merge provided args with enforced page=1
        var baseObj = ConvertToJsonObject(args) ?? new JObject();
        baseObj["page"] = 1;
        if (pageSize.HasValue) baseObj["pageSize"] = pageSize.Value;
        return ExecuteCollection(baseObj);
    }

    /// <summary>
    /// SDK.Entities.Todo.page(pageNumber, pageSize?, args?) - collection with explicit page.
    /// </summary>
    public object page(int pageNumber, int? pageSize = null, object? args = null)
    {
        if (pageNumber <= 0) throw new JavaScriptException("Page number must be > 0");
        var baseObj = ConvertToJsonObject(args) ?? new JObject();
        baseObj["page"] = pageNumber;
        if (pageSize.HasValue) baseObj["pageSize"] = pageSize.Value;
        return ExecuteCollection(baseObj);
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
    var args = new JObject { ["id"] = id };

        // Extract options if provided
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson != null)
            {
                if (optsJson.TryGetValue("set", StringComparison.OrdinalIgnoreCase, out var setToken))
                    args["set"] = setToken?.DeepClone();
                if (optsJson.TryGetValue("with", StringComparison.OrdinalIgnoreCase, out var withToken))
                    args["with"] = withToken?.DeepClone();
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
        var args = new JObject
        {
            ["model"] = ConvertToJsonNode(model)
        };

        // Extract options
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson != null && optsJson.TryGetValue("set", StringComparison.OrdinalIgnoreCase, out var setToken))
            {
                args["set"] = setToken?.DeepClone();
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
    var args = new JObject { ["id"] = id };

        // Extract options
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson != null && optsJson.TryGetValue("set", StringComparison.OrdinalIgnoreCase, out var setToken))
            {
                args["set"] = setToken?.DeepClone();
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
        JArray idsArray;
        if (idsArg is IEnumerable<object> enumerable)
        {
            // Materialize to object[] with non-null string representations, preserving order
            var materialized = enumerable.Select(id => (object?)(id?.ToString() ?? string.Empty)).ToArray();
            idsArray = new JArray(materialized!); // safe: elements are non-null strings
        }
        else
        {
            var idsJson = ConvertToJsonNode(idsArg);
            if (idsJson is not JArray arr)
            {
                throw new JavaScriptException("IDs must be an array");
            }
            idsArray = arr;
        }

        var args = new JObject { ["ids"] = idsArray };

        // Extract options
        if (options != null)
        {
            var optsJson = ConvertToJsonObject(options);
            if (optsJson != null && optsJson.TryGetValue("set", StringComparison.OrdinalIgnoreCase, out var setToken))
            {
                args["set"] = setToken?.DeepClone();
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

    private object ExecuteCollection(object? args)
    {
        _metrics.IncrementCalls();
        var tool = FindTool(EntityEndpointOperationKind.Collection);
        var argsObj = args != null ? _json.FromObject(args) : new JObject();
        var result = _executor.ExecuteAsync(tool.Name, (JObject)argsObj, default).GetAwaiter().GetResult();
        return ConvertToJavaScriptObject(result);
    }

    private JObject? ConvertToJsonObject(object? obj) => obj == null ? null : (_json.FromObject(obj) as JObject);
    private JToken? ConvertToJsonNode(object? obj) => obj == null ? null : (_json.FromObject(obj));

    private object ConvertToJavaScriptObject(McpToolExecutionResult result)
    {
        if (!result.Success)
        {
            var errorMsg = !string.IsNullOrWhiteSpace(result.ErrorCode)
                ? $"{result.ErrorCode}: {result.ErrorMessage}"
                : result.ErrorMessage ?? "Operation failed";

            throw new JavaScriptException(errorMsg);
        }

        if (result.Payload == null) return new {};

        var token = (result.Payload as JToken) ?? JToken.Parse(result.Payload.ToString());

        // If collection endpoint produced array, wrap with items + count for stability
        if (token is JArray arr)
        {
            var wrapper = new JObject
            {
                ["items"] = arr,
                ["count"] = arr.Count
            };
            return _json.ToDynamic(wrapper) ?? new {};
        }

        return _json.ToDynamic(token) ?? new {};
    }

    private int ExtractCount(McpToolExecutionResult result)
    {
        if (!result.Success)
        {
            return 0;
        }

        if (result.Payload is JObject jobj && jobj.TryGetValue("count", StringComparison.OrdinalIgnoreCase, out var countToken))
        {
            if (countToken.Type == JTokenType.Integer && (int)countToken >= 0)
                return (int)countToken;
        }

        // Fallback: assume 1 if we got here successfully
        return 1;
    }
}
