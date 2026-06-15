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
    public required MethodInfo Method { get; init; }
    public required IReadOnlyList<McpCustomToolParameter> Parameters { get; init; }
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
    CancellationToken
}
