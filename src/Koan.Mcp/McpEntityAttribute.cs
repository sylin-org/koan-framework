using System;

namespace Koan.Mcp;

/// <summary>
/// Marks an entity for Model Context Protocol exposure.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpEntityAttribute : Attribute
{
    /// <summary>
    /// Optional tool namespace override. Defaults to the entity name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Human readable description surfaced to MCP clients.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Scopes required before tools are exposed.
    /// </summary>
    public string[] RequiredScopes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When false mutation operations are filtered from the registry.
    /// </summary>
    public bool AllowMutations { get; set; } = true;

    /// <summary>
    /// Allows opting into STDIO transport at the entity level when server defaults disable it.
    /// </summary>
    public bool EnableStdio { get; set; } = true;

    /// <summary>
    /// Optional raw schema override. When provided the registry bypasses automatic schema generation.
    /// </summary>
    public string? SchemaOverride { get; set; }

    /// <summary>
    /// Optional prefix appended to generated tool names for namespacing.
    /// </summary>
    public string? ToolPrefix { get; set; }
}
