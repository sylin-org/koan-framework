using System;

namespace Koan.Mcp;

/// <summary>
/// Marks a public static method as a custom MCP tool — a verb that is NOT an entity CRUD/query
/// operation. <c>[McpEntity]</c> auto-generates entity tools; <c>[McpTool]</c> exposes a hand-written
/// verb (e.g. a semantic-search or action endpoint) over the same <c>tools/list</c> + <c>tools/call</c>
/// surface.
/// </summary>
/// <remarks>
/// The method is discovered by <see cref="Koan.Mcp.CustomTools.McpCustomToolRegistry"/>. Its parameters
/// are bound at call time: a parameter of type <see cref="IServiceProvider"/> receives the request's
/// service provider and a <see cref="System.Threading.CancellationToken"/> receives the call's token;
/// every other parameter is bound from the call <c>arguments</c> object by name (and contributes to the
/// generated input schema). The return value (synchronous or <c>Task&lt;T&gt;</c>) is serialized as the
/// tool result. Give optional arguments a default value (e.g. <c>string? topic = null</c>) so they are
/// advertised as optional in the schema.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Tool name advertised to MCP clients. Defaults to the method name when unset.</summary>
    public string? Name { get; set; }

    /// <summary>Human-readable description shown during tool discovery.</summary>
    public string? Description { get; set; }

    /// <summary>OAuth scopes required to invoke this tool. Empty means no scope gate.</summary>
    public string[] RequiredScopes { get; set; } = Array.Empty<string>();

    /// <summary>True when the tool mutates state. Advertised in the tool metadata.</summary>
    public bool IsMutation { get; set; }
}
