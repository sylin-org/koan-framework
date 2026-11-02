using System;

namespace Koan.Mcp;

/// <summary>
/// Marks a public static method as a custom MCP tool with no route: a verb that is neither an entity
/// CRUD/query operation nor something an application also serves over HTTP. <c>[McpEntity]</c> generates
/// entity tools, and <c>[Tool]</c> on a controller action makes a route a tool; <c>[McpTool]</c> exposes a
/// hand-written verb over the same <c>tools/list</c> + <c>tools/call</c> surface. Its authority is the one
/// gate, declared with <c>[Access]</c> or <c>[RequireScope]</c> on the method or its class (ARCH-0141).
/// </summary>
/// <remarks>
/// The method is discovered by <see cref="Koan.Mcp.CustomTools.McpCustomToolRegistry"/>. Its parameters
/// are bound at call time: a parameter of type <see cref="IServiceProvider"/> receives the request's
/// service provider and a <see cref="System.Threading.CancellationToken"/> receives the call's token;
/// every other parameter is bound from the call <c>arguments</c> object by name (and contributes to the
/// generated input schema). A value that cannot bind is refused, naming the parameter, and so is a missing
/// required one. The return value (synchronous or <c>Task&lt;T&gt;</c>) is serialized as the tool result. Give
/// optional arguments a default value (e.g. <c>string? topic = null</c>) so they are advertised as optional in
/// the schema.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpToolAttribute : Attribute
{
    /// <summary>Tool name advertised to MCP clients: lower-case words joined by underscores, at most 64 characters. Defaults to the method name in those words when unset.</summary>
    public string? Name { get; set; }

    /// <summary>Human-readable description shown during tool discovery.</summary>
    public string? Description { get; set; }

    /// <summary>True when the tool mutates state. Advertised in the tool metadata.</summary>
    public bool IsMutation { get; set; }
}
