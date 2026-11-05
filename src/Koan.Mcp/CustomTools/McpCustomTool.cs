using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing.Patterns;
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
    public bool IsMutation { get; init; }

    /// <summary>What the gate is asked for this verb: read, write or remove (ARCH-0141).</summary>
    public required string GateAction { get; init; }

    /// <summary>The route this tool is a request to; null for a verb with no route.</summary>
    internal RouteTool? Route { get; init; }

    /// <summary>Whether this is the edge's <c>read</c>, which reads any address as its caller and answers to each route's own gate (AI-0042).</summary>
    internal bool Reads { get; init; }

    // AN4 — opt-in spec annotations (null = unmarked → omitted from the wire annotations object).
    public bool? ReadOnly { get; init; }
    public bool? Destructive { get; init; }
    public bool? Idempotent { get; init; }
    public bool? OpenWorld { get; init; }

    public required MethodInfo Method { get; init; }
    public required IReadOnlyList<McpCustomToolParameter> Parameters { get; init; }

    /// <summary>P3.2 — when set (from <c>[McpOperationalToolset(key)]</c>), this verb belongs to a config-gated
    /// operational toolset: it is visible/invocable ONLY when <c>Koan:Mcp:Operations:{key}</c> is enabled. Null for
    /// an ordinary custom verb.</summary>
    public string? OperationalToolsetKey { get; init; }
}

/// <summary>Where a <c>[Tool]</c> action answers: what a call needs to reach it as a request (ARCH-0141), and whether an <c>address</c>
/// may name its route values (AI-0042).</summary>
internal sealed record RouteTool(ControllerActionDescriptor Action, string HttpMethod, RoutePattern Pattern, IReadOnlySet<string> QueryFields, string? BodyField, bool TakesAddress = false)
{
    /// <summary>The policy ASP.NET's middleware applies to this action; null when it applies none.</summary>
    public async Task<AuthorizationPolicy?> Policy(IAuthorizationPolicyProvider policies)
    {
        if (Action.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return null;
        return await AuthorizationPolicy.CombineAsync(policies, Action.EndpointMetadata.OfType<IAuthorizeData>().ToArray()).ConfigureAwait(false)
               ?? await policies.GetFallbackPolicyAsync().ConfigureAwait(false);
    }
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
