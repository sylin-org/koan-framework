using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// A discovered <c>[McpTool]</c> verb: its advertised name + schema plus the binding needed to invoke
/// the backing static method. Built by <see cref="McpCustomToolRegistry"/>, invoked by
/// <see cref="McpCustomToolInvoker"/>.
/// </summary>
public sealed class McpCustomTool
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required JObject InputSchema { get; init; }
    public IReadOnlyList<string> RequiredScopes { get; init; } = Array.Empty<string>();
    public bool IsMutation { get; init; }

    // AN4 — opt-in spec annotations (null = unmarked → omitted from the wire annotations object).
    public bool? ReadOnly { get; init; }
    public bool? Destructive { get; init; }
    public bool? Idempotent { get; init; }

    public required MethodInfo Method { get; init; }
    public required IReadOnlyList<McpCustomToolParameter> Parameters { get; init; }

    /// <summary>P3.2 — when set (from <c>[McpOperationalToolset(key)]</c>), this verb belongs to a config-gated
    /// operational toolset: it is visible/invocable ONLY when <c>Koan:Mcp:Operations:{key}</c> is enabled. Null for
    /// an ordinary custom verb.</summary>
    public string? OperationalToolsetKey { get; init; }
}

/// <summary>One parameter of a custom tool method and how it is supplied at call time.</summary>
public sealed class McpCustomToolParameter
{
    public required string Name { get; init; }
    public required Type Type { get; init; }
    public required McpCustomToolParameterSource Source { get; init; }
    public bool IsOptional { get; init; }
    public object? DefaultValue { get; init; }
}

/// <summary>Where a custom-tool parameter's value comes from.</summary>
public enum McpCustomToolParameterSource
{
    /// <summary>Bound from the call <c>arguments</c> object by name (contributes to the input schema).</summary>
    Arguments,

    /// <summary>Injected: the request's <see cref="IServiceProvider"/>.</summary>
    ServiceProvider,

    /// <summary>Injected: the call's <see cref="System.Threading.CancellationToken"/>.</summary>
    CancellationToken,

    /// <summary>Injected: the caller's <see cref="System.Security.Claims.ClaimsPrincipal"/> (P3.2 — null at an
    /// anonymous/STDIO edge). Lets a custom verb gate/audit by the caller's subject without an HttpContext.</summary>
    Principal
}
