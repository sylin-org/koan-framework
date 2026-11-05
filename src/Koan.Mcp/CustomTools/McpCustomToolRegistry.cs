using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Koan.Mcp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// Discovers public static methods decorated with <see cref="McpToolAttribute"/> across loaded
/// assemblies, and the routes <c>[Tool]</c> makes tools, and exposes them as custom MCP tools (verbs
/// that are not entity CRUD operations). Discovery is lazy + thread-safe and uses the same assembly
/// source as the entity registry, so lazily-loaded capability assemblies are included.
/// </summary>
public sealed class McpCustomToolRegistry
{
    private readonly ILogger<McpCustomToolRegistry> _logger;
    private readonly IActionDescriptorCollectionProvider? _actions;
    private readonly IOptions<McpServerOptions>? _options;
    private readonly object _sync = new();
    private IReadOnlyList<McpCustomTool>? _tools;
    private IReadOnlyDictionary<string, McpCustomTool>? _index;

    public McpCustomToolRegistry(ILogger<McpCustomToolRegistry> logger, IActionDescriptorCollectionProvider? actions = null, IOptions<McpServerOptions>? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _actions = actions;
        _options = options;
    }

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
                index.TryAdd(tool.Name, tool); // McpToolStartCheck stops the host at start over a duplicate.
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

        var addressed = _options?.Value.ReadByAddress == true;
        if (_actions is not null) result.AddRange(RouteTools.Discover(_actions, addressed));
        if (addressed) result.Add(AddressReader.Tool);

        _logger.LogDebug("Discovered {Count} custom MCP tool(s).", result.Count);
        return result;
    }

    private McpCustomTool? Build(MethodInfo method, McpToolAttribute attribute)
    {
        // Nothing would enforce it: only ASP.NET's middleware reads [Authorize], on a route.
        if (method.IsDefined(typeof(AuthorizeAttribute), true) || method.ReflectedType?.IsDefined(typeof(AuthorizeAttribute), true) == true)
            throw new InvalidOperationException($"[Authorize] on [McpTool] {method.ReflectedType?.Name}.{method.Name} would answer to nothing. Declare [Access(...)] or [RequireScope(...)], or make it a route with [Tool].");

        var name = string.IsNullOrWhiteSpace(attribute.Name) ? ToolName.Of(method.Name) : attribute.Name!.Trim();

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
                var paramSchema = BuildParameterSchema(parameter.ParameterType);
                // WEB-0072: describe a custom verb's parameter via [McpDescription] (or [Description]) on the parameter.
                var description = ParameterDescription(parameter);
                if (description is not null) paramSchema["description"] = description;
                properties[argName] = paramSchema;
                if (!isOptional) required.Add(argName);
            }
        }

        if (attribute.IsMutation)
        {
            properties[Koan.Mcp.Execution.McpDryRun.ArgumentName] = new JObject
            {
                ["type"] = "boolean",
                ["description"] = Koan.Mcp.Execution.McpDryRun.SchemaDescription
            };
        }

        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
        if (required.Count > 0) schema["required"] = required;

        return Assemble(name, attribute.Description, schema, method, attribute.IsMutation, parameters: parameters,
            gateAction: attribute.IsMutation ? EntityAuthorizeActions.Write : EntityAuthorizeActions.Read);
    }

    /// <summary>A tool from what its definition says. AN4's spec annotations are opt-in markers on the method; a route
    /// also says what its HTTP method promises. A verb on an [McpOperationalToolset]-marked Toolset is config-gated by that
    /// toolset's key (P3.2).</summary>
    internal static McpCustomTool Assemble(string name, string? description, JObject schema, MethodInfo method, bool isMutation, string gateAction,
        bool? readOnly = null, bool? destructive = null, bool? idempotent = null, bool? openWorld = null, RouteTool? route = null, IReadOnlyList<McpCustomToolParameter>? parameters = null,
        bool reads = false) => new()
    {
        Name = name,
        Description = description,
        InputSchema = schema,
        IsMutation = isMutation,
        GateAction = gateAction,
        ReadOnly = method.GetCustomAttribute<McpReadOnlyAttribute>() is not null ? true : readOnly,
        Destructive = method.GetCustomAttribute<McpDestructiveAttribute>() is not null ? true : destructive,
        Idempotent = method.GetCustomAttribute<McpIdempotentAttribute>() is not null ? true : idempotent,
        OpenWorld = openWorld,
        OperationalToolsetKey = method.DeclaringType?.GetCustomAttribute<McpOperationalToolsetAttribute>()?.Key,
        Method = method,
        Parameters = parameters ?? [],
        Route = route,
        Reads = reads,
    };

    private static string? ParameterDescription(ParameterInfo parameter)
    {
        var mcp = parameter.GetCustomAttribute<McpDescriptionAttribute>();
        if (mcp is not null) return mcp.Description;
        var description = parameter.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
        return string.IsNullOrWhiteSpace(description?.Description) ? null : description!.Description;
    }

    private static McpCustomToolParameterSource ResolveSource(Type type)
    {
        if (type == typeof(CancellationToken)) return McpCustomToolParameterSource.CancellationToken;
        if (type == typeof(IServiceProvider)) return McpCustomToolParameterSource.ServiceProvider;
        if (type == typeof(ClaimsPrincipal)) return McpCustomToolParameterSource.Principal;
        return McpCustomToolParameterSource.Arguments;
    }

    internal static JObject BuildParameterSchema(Type type)
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
