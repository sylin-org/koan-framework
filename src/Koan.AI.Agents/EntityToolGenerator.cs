using System.Reflection;
using System.Text.Json;

namespace Koan.AI.Agents;

/// <summary>
/// Generates tool definitions and execution delegates from Entity&lt;T&gt; types.
/// The AI model sees tool descriptions with JSON-schema parameters; when it calls a tool,
/// the generator routes to the appropriate Entity&lt;T&gt; static method.
/// </summary>
internal static class EntityToolGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Generates tool definitions for an entity binding (type + write flag).
    /// Returns tool metadata for the AI model and execution delegates.
    /// </summary>
    public static IReadOnlyList<GeneratedTool> Generate(EntityBinding binding)
    {
        var entityType = binding.EntityType;
        var typeName = entityType.Name.ToLowerInvariant();
        var tools = new List<GeneratedTool>();

        // Always generate read tools
        tools.Add(BuildGetTool(entityType, typeName));
        tools.Add(BuildQueryTool(entityType, typeName));

        // Write tools only if allowed
        if (binding.AllowWrite)
        {
            tools.Add(BuildSaveTool(entityType, typeName));
            tools.Add(BuildDeleteTool(entityType, typeName));
        }

        return tools;
    }

    /// <summary>
    /// Generates a search tool for a vector-enabled entity type.
    /// </summary>
    public static GeneratedTool GenerateSearchTool(Type entityType)
    {
        var typeName = entityType.Name.ToLowerInvariant();

        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["text"] = new { type = "string", description = "Search query text" },
                ["top_k"] = new { type = "integer", description = "Maximum results to return (default: 5)" }
            },
            required = new[] { "text" }
        };

        return new GeneratedTool(
            Name: $"{typeName}_search",
            Description: $"Semantic vector search over {entityType.Name} entities. " +
                         $"Returns the most relevant results based on meaning similarity.",
            ParametersSchema: JsonSerializer.Serialize(schema, JsonOptions),
            Execute: async (args, ct) => await ExecuteSearch(entityType, args, ct));
    }

    // ── Tool builders ──

    private static GeneratedTool BuildGetTool(Type entityType, string typeName)
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["id"] = new { type = "string", description = $"The ID of the {entityType.Name} to retrieve" }
            },
            required = new[] { "id" }
        };

        return new GeneratedTool(
            Name: $"{typeName}_get",
            Description: $"Retrieve a single {entityType.Name} entity by its ID.",
            ParametersSchema: JsonSerializer.Serialize(schema, JsonOptions),
            Execute: async (args, ct) => await ExecuteGet(entityType, args, ct));
    }

    private static GeneratedTool BuildQueryTool(Type entityType, string typeName)
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["limit"] = new { type = "integer", description = $"Maximum number of {entityType.Name} entities to return (default: 10)" }
            }
        };

        return new GeneratedTool(
            Name: $"{typeName}_query",
            Description: $"Query {entityType.Name} entities. Returns up to 'limit' results.",
            ParametersSchema: JsonSerializer.Serialize(schema, JsonOptions),
            Execute: async (args, ct) => await ExecuteQuery(entityType, args, ct));
    }

    private static GeneratedTool BuildSaveTool(Type entityType, string typeName)
    {
        var properties = BuildPropertiesSchema(entityType);

        var schema = new
        {
            type = "object",
            properties,
            required = new[] { "data" }
        };

        return new GeneratedTool(
            Name: $"{typeName}_save",
            Description: $"Create or update a {entityType.Name} entity. Provide the entity data as a JSON object.",
            ParametersSchema: JsonSerializer.Serialize(schema, JsonOptions),
            Execute: async (args, ct) => await ExecuteSave(entityType, args, ct));
    }

    private static GeneratedTool BuildDeleteTool(Type entityType, string typeName)
    {
        var schema = new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                ["id"] = new { type = "string", description = $"The ID of the {entityType.Name} to delete" }
            },
            required = new[] { "id" }
        };

        return new GeneratedTool(
            Name: $"{typeName}_delete",
            Description: $"Delete a {entityType.Name} entity by its ID.",
            ParametersSchema: JsonSerializer.Serialize(schema, JsonOptions),
            Execute: async (args, ct) => await ExecuteDelete(entityType, args, ct));
    }

    // ── Property schema generation ──

    private static Dictionary<string, object> BuildPropertiesSchema(Type entityType)
    {
        var entityProperties = new Dictionary<string, object>();
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanWrite) continue;
            entityProperties[prop.Name] = new
            {
                type = MapClrTypeToJsonType(prop.PropertyType),
                description = $"{entityType.Name}.{prop.Name}"
            };
        }

        return new Dictionary<string, object>
        {
            ["data"] = new
            {
                type = "object",
                description = $"The {entityType.Name} entity data",
                properties = entityProperties
            }
        };
    }

    private static string MapClrTypeToJsonType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying switch
        {
            _ when underlying == typeof(string) => "string",
            _ when underlying == typeof(int) || underlying == typeof(long)
                || underlying == typeof(short) || underlying == typeof(byte) => "integer",
            _ when underlying == typeof(float) || underlying == typeof(double)
                || underlying == typeof(decimal) => "number",
            _ when underlying == typeof(bool) => "boolean",
            _ when underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) => "string",
            _ when underlying == typeof(Guid) => "string",
            _ when underlying.IsArray || (underlying.IsGenericType
                && underlying.GetGenericTypeDefinition() == typeof(List<>)) => "array",
            _ => "string"
        };
    }

    // ── Tool execution ──

    private static async Task<string> ExecuteGet(
        Type entityType, Dictionary<string, object?> args, CancellationToken ct)
    {
        if (!args.TryGetValue("id", out var idObj) || idObj is null)
            return JsonSerializer.Serialize(new { error = "Missing required parameter: id" });

        var id = idObj.ToString()!;

        try
        {
            // Find the Entity<T, TKey> base and invoke Get(TKey)
            var getMethod = FindStaticMethod(entityType, "Get", typeof(string), typeof(CancellationToken));
            if (getMethod is null)
            {
                // Try Guid key
                if (Guid.TryParse(id, out var guidId))
                {
                    getMethod = FindStaticMethod(entityType, "Get", typeof(Guid), typeof(CancellationToken));
                    if (getMethod is not null)
                    {
                        var guidResult = await InvokeAsyncMethod(getMethod, null, [guidId, ct]);
                        return SerializeResult(guidResult);
                    }
                }
                return JsonSerializer.Serialize(new { error = $"Get method not found on {entityType.Name}" });
            }

            var result = await InvokeAsyncMethod(getMethod, null, [id, ct]);
            return SerializeResult(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private static async Task<string> ExecuteQuery(
        Type entityType, Dictionary<string, object?> args, CancellationToken ct)
    {
        var limit = 10;
        if (args.TryGetValue("limit", out var limitObj) && limitObj is not null)
        {
            if (limitObj is JsonElement je && je.TryGetInt32(out var parsed))
                limit = parsed;
            else if (int.TryParse(limitObj.ToString(), out var parsedInt))
                limit = parsedInt;
        }

        try
        {
            // Call Entity<T>.All(DataQueryOptions, CancellationToken) with pagination
            var allMethod = FindStaticMethod(entityType, "All",
                typeof(Koan.Data.Abstractions.DataQueryOptions), typeof(CancellationToken));

            if (allMethod is not null)
            {
                var options = new Koan.Data.Abstractions.DataQueryOptions(page: 1, pageSize: limit);
                var result = await InvokeAsyncMethod(allMethod, null, [options, ct]);
                return SerializeResult(result);
            }

            // Fallback: All(CancellationToken)
            var simpleAllMethod = FindStaticMethod(entityType, "All", typeof(CancellationToken));
            if (simpleAllMethod is not null)
            {
                var result = await InvokeAsyncMethod(simpleAllMethod, null, [ct]);
                // Apply client-side limit
                if (result is System.Collections.IEnumerable enumerable)
                {
                    var items = new List<object>();
                    var count = 0;
                    foreach (var item in enumerable)
                    {
                        if (count++ >= limit) break;
                        items.Add(item);
                    }
                    return SerializeResult(items);
                }
                return SerializeResult(result);
            }

            return JsonSerializer.Serialize(new { error = $"All method not found on {entityType.Name}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private static async Task<string> ExecuteSave(
        Type entityType, Dictionary<string, object?> args, CancellationToken ct)
    {
        if (!args.TryGetValue("data", out var dataObj) || dataObj is null)
            return JsonSerializer.Serialize(new { error = "Missing required parameter: data" });

        try
        {
            // Deserialize the data into the entity type
            var json = dataObj is JsonElement je ? je.GetRawText() : JsonSerializer.Serialize(dataObj);
            var entity = JsonSerializer.Deserialize(json, entityType, JsonOptions);
            if (entity is null)
                return JsonSerializer.Serialize(new { error = $"Failed to deserialize data as {entityType.Name}" });

            // Call Entity<T>.Upsert(entity, ct)
            var upsertMethod = FindStaticMethod(entityType, "UpsertAsync", entityType, typeof(CancellationToken));
            if (upsertMethod is not null)
            {
                var result = await InvokeAsyncMethod(upsertMethod, null, [entity, ct]);
                return SerializeResult(result);
            }

            return JsonSerializer.Serialize(new { error = $"UpsertAsync method not found on {entityType.Name}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private static async Task<string> ExecuteDelete(
        Type entityType, Dictionary<string, object?> args, CancellationToken ct)
    {
        if (!args.TryGetValue("id", out var idObj) || idObj is null)
            return JsonSerializer.Serialize(new { error = "Missing required parameter: id" });

        var id = idObj.ToString()!;

        try
        {
            // Call Entity<T>.Remove(id, ct)
            var removeMethod = FindStaticMethod(entityType, "Remove", typeof(string), typeof(CancellationToken));
            if (removeMethod is null)
            {
                if (Guid.TryParse(id, out var guidId))
                {
                    removeMethod = FindStaticMethod(entityType, "Remove", typeof(Guid), typeof(CancellationToken));
                    if (removeMethod is not null)
                    {
                        var guidResult = await InvokeAsyncMethod(removeMethod, null, [guidId, ct]);
                        return SerializeResult(guidResult);
                    }
                }
                return JsonSerializer.Serialize(new { error = $"Remove method not found on {entityType.Name}" });
            }

            var result = await InvokeAsyncMethod(removeMethod, null, [id, ct]);
            return SerializeResult(result);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private static async Task<string> ExecuteSearch(
        Type entityType, Dictionary<string, object?> args, CancellationToken ct)
    {
        if (!args.TryGetValue("text", out var textObj) || textObj is null)
            return JsonSerializer.Serialize(new { error = "Missing required parameter: text" });

        var text = textObj.ToString()!;
        var topK = 5;
        if (args.TryGetValue("top_k", out var topKObj) && topKObj is not null)
        {
            if (topKObj is JsonElement je && je.TryGetInt32(out var parsed))
                topK = parsed;
            else if (int.TryParse(topKObj.ToString(), out var parsedInt))
                topK = parsedInt;
        }

        try
        {
            // Generate embedding
            var embedding = await Koan.AI.Client.Embed(text, ct);

            // Invoke Vector<T>.Search via reflection
            var vectorType = typeof(Koan.Data.Vector.Vector<>).MakeGenericType(entityType);

            var isAvailableProp = vectorType.GetProperty("IsAvailable");
            if (isAvailableProp is not null && !(bool)(isAvailableProp.GetValue(null) ?? false))
                return JsonSerializer.Serialize(new { error = $"Vector search not available for {entityType.Name}" });

            var searchMethod = vectorType.GetMethod("Search", [
                typeof(float[]),
                typeof(string),
                typeof(double?),
                typeof(int?),
                typeof(object),
                typeof(string),
                typeof(string),
                typeof(CancellationToken)
            ]);

            if (searchMethod is null)
                return JsonSerializer.Serialize(new { error = "Vector.Search method not found" });

            var task = searchMethod.Invoke(null, [embedding, text, null, topK, null, null, null, ct]);
            if (task is null)
                return JsonSerializer.Serialize(new { results = Array.Empty<object>() });

            await (Task)task;

            var resultProp = task.GetType().GetProperty("Result");
            var queryResult = resultProp?.GetValue(task);
            return SerializeResult(queryResult);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    // ── Reflection helpers ──

    private static MethodInfo? FindStaticMethod(Type type, string name, params Type[] parameterTypes)
    {
        // Walk the type hierarchy to find static methods (Entity<T> defines them)
        var current = type;
        while (current is not null)
        {
            var method = current.GetMethod(name,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy,
                null, parameterTypes, null);
            if (method is not null)
                return method;
            current = current.BaseType;
        }
        return null;
    }

    private static async Task<object?> InvokeAsyncMethod(
        MethodInfo method, object? target, object?[] parameters)
    {
        var result = method.Invoke(target, parameters);
        if (result is Task task)
        {
            await task;
            var resultProp = task.GetType().GetProperty("Result");
            return resultProp?.GetValue(task);
        }
        return result;
    }

    private static string SerializeResult(object? result)
    {
        if (result is null)
            return JsonSerializer.Serialize(new { result = (object?)null });

        try
        {
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch
        {
            return JsonSerializer.Serialize(new { result = result.ToString() });
        }
    }
}

/// <summary>
/// A tool generated from entity reflection, ready for agent use.
/// Contains both the metadata for the AI model and the execution delegate.
/// </summary>
internal sealed record GeneratedTool(
    string Name,
    string Description,
    string ParametersSchema,
    Func<Dictionary<string, object?>, CancellationToken, Task<string>> Execute);
