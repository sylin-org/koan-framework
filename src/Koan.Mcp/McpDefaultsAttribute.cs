using System;

namespace Koan.Mcp;

/// <summary>
/// Assembly-level defaults for MCP behavior.
/// Can be overridden by configuration or entity-level attributes.
/// </summary>
/// <example>
/// <code>
/// [assembly: McpDefaults(Exposure = "code")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class McpDefaultsAttribute : Attribute
{
    private McpExposureMode? _exposure;

    /// <summary>
    /// Default exposure mode for all entities in this assembly.
    /// Valid values: "auto", "code", "tools", "full", or null to use framework default.
    /// </summary>
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
    /// Require authentication by default for all MCP operations.
    /// </summary>
    public bool? RequireAuthentication { get; set; }

    /// <summary>
    /// Typed accessor for internal use.
    /// </summary>
    internal McpExposureMode? ExposureMode => _exposure;
}
