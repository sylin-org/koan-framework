using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// Discovers public static methods decorated with <see cref="McpToolAttribute"/> across loaded
/// assemblies and exposes them as custom MCP tools (verbs that are not entity CRUD operations).
/// Discovery is lazy + thread-safe and uses the same assembly source as the entity registry, so
/// lazily-loaded capability assemblies are included.
/// </summary>
public sealed class McpCustomToolRegistry
{
    private readonly ILogger<McpCustomToolRegistry> _logger;
    private readonly object _sync = new();
    private IReadOnlyList<McpCustomTool>? _tools;
    private IReadOnlyDictionary<string, McpCustomTool>? _index;

    public McpCustomToolRegistry(ILogger<McpCustomToolRegistry> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>All discovered custom tools.</summary>
    public IReadOnlyList<McpCustomTool> Tools
    {
        get { EnsureBuilt(); return _tools!; }
    }

    /// <summary>Resolve a custom tool by its advertised name.</summary>
    public bool TryGet(string name, out McpCustomTool tool)
    {
        EnsureBuilt();
        if (!string.IsNullOrWhiteSpace(name) && _index!.TryGetValue(name, out var found))
        {
            tool = found;
            return true;
        }

        tool = null!;
        return false;
    }

    private void EnsureBuilt()
    {
        if (_tools is not null) return;
        lock (_sync)
        {
            if (_tools is not null) return;

            var tools = Discover();
            var index = new Dictionary<string, McpCustomTool>(StringComparer.OrdinalIgnoreCase);
            foreach (var tool in tools)
            {
                if (!index.ContainsKey(tool.Name)) index[tool.Name] = tool;
                else _logger.LogWarning("Duplicate [McpTool] name '{Name}'; keeping the first occurrence.", tool.Name);
            }

            _tools = tools;
            _index = index;
        }
    }

    private List<McpCustomTool> Discover()
    {
        var result = new List<McpCustomTool>();

        foreach (var assembly in AssemblyCache.Instance.GetAllAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type is null || !type.IsClass) continue;

                // The established standalone-verb path: public static [McpTool] methods anywhere.
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var attribute = method.GetCustomAttribute<McpToolAttribute>();
                    if (attribute is null) continue;

                    var tool = Build(method, attribute);
                    if (tool is not null) result.Add(tool);
                }

                // ARCH-0092 §H: custom [McpTool] verbs as INSTANCE methods on a Toolset subclass — invoked on
                // a DI-created toolset instance (this-context + injected dependencies).
                if (!type.IsAbstract && typeof(Toolset).IsAssignableFrom(type))
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        var attribute = method.GetCustomAttribute<McpToolAttribute>();
                        if (attribute is null) continue;

                        var tool = Build(method, attribute);
                        if (tool is not null) result.Add(tool);
                    }
                }
            }
        }

        _logger.LogDebug("Discovered {Count} custom MCP tool(s).", result.Count);
        return result;
    }

    private McpCustomTool? Build(MethodInfo method, McpToolAttribute attribute)
    {
        var name = string.IsNullOrWhiteSpace(attribute.Name) ? method.Name : attribute.Name!.Trim();

        var parameters = new List<McpCustomToolParameter>();
        var properties = new JObject();
        var required = new JArray();

        foreach (var parameter in method.GetParameters())
        {
            var source = ResolveSource(parameter.ParameterType);
            var isOptional = parameter.HasDefaultValue || Nullable.GetUnderlyingType(parameter.ParameterType) is not null;

            parameters.Add(new McpCustomToolParameter
            {
                Name = parameter.Name ?? "arg",
                Type = parameter.ParameterType,
                Source = source,
                IsOptional = isOptional,
                DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null
            });

            if (source == McpCustomToolParameterSource.Arguments && parameter.Name is { Length: > 0 } argName)
            {
                properties[argName] = BuildParameterSchema(parameter.ParameterType);
                if (!isOptional) required.Add(argName);
            }
        }

        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required.Count > 0) schema["required"] = required;

        return new McpCustomTool
        {
            Name = name,
            Description = attribute.Description,
            InputSchema = schema,
            RequiredScopes = attribute.RequiredScopes ?? Array.Empty<string>(),
            IsMutation = attribute.IsMutation,
            // AN4: opt-in spec annotations from the method markers (unmarked stays null → omitted).
            ReadOnly = method.GetCustomAttribute<McpReadOnlyAttribute>() is not null ? true : null,
            Destructive = method.GetCustomAttribute<McpDestructiveAttribute>() is not null ? true : null,
            Idempotent = method.GetCustomAttribute<McpIdempotentAttribute>() is not null ? true : null,
            Method = method,
            Parameters = parameters
        };
    }

    private static McpCustomToolParameterSource ResolveSource(Type type)
    {
        if (type == typeof(CancellationToken)) return McpCustomToolParameterSource.CancellationToken;
        if (type == typeof(IServiceProvider)) return McpCustomToolParameterSource.ServiceProvider;
        return McpCustomToolParameterSource.Arguments;
    }

    private static JObject BuildParameterSchema(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t == typeof(string)) return new JObject { ["type"] = "string" };
        if (t == typeof(bool)) return new JObject { ["type"] = "boolean" };
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte))
            return new JObject { ["type"] = "integer" };
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal))
            return new JObject { ["type"] = "number" };
        if (t.IsEnum) return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(t)) };
        if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t)) return new JObject { ["type"] = "array" };
        return new JObject { ["type"] = "object" };
    }
}
