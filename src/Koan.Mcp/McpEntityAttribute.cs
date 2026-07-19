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
    /// When false mutation operations are filtered from the registry.
    /// </summary>
    public bool AllowMutations { get; set; } = true;

    /// <summary>
    /// Optional raw schema override. When provided the registry bypasses automatic schema generation.
    /// </summary>
    public string? SchemaOverride { get; set; }

    /// <summary>
    /// Optional prefix appended to generated tool names for namespacing.
    /// </summary>
    public string? ToolPrefix { get; set; }

    private McpExposureMode? _exposure;

    /// <summary>
    /// Determines how this entity is exposed via MCP (Auto, Code, Tools, Full).
    /// Null = inherit from assembly/configuration defaults.
    /// Valid string values: "auto", "code", "tools", "full"
    /// </summary>
    /// <example>
    /// <code>
    /// [McpEntity(Name = "Todo", Exposure = "code")]
    /// [McpEntity(Name = "AuditLog", Exposure = McpExposureMode.Tools)]
    /// </code>
    /// </example>
    public object? Exposure
    {
        get => _exposure;
        set => _exposure = value switch
        {
            null => null,
            McpExposureMode mode => mode,
            string s => s.ToLowerInvariant() switch
            {
                "auto" => McpExposureMode.Auto,
                "code" => McpExposureMode.Code,
                "tools" => McpExposureMode.Tools,
                "full" => McpExposureMode.Full,
                _ => throw new ArgumentException(
                    $"Invalid exposure mode '{s}'. Valid values: auto, code, tools, full",
                    nameof(value))
            },
            _ => throw new ArgumentException(
                "Exposure must be McpExposureMode enum or string literal (auto, code, tools, full)",
                nameof(value))
        };
    }

    /// <summary>
    /// Typed accessor for internal use.
    /// </summary>
    internal McpExposureMode? ExposureMode => _exposure;
}
