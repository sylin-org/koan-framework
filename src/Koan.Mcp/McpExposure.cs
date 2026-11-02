using System;
using System.Linq;
using Koan.Mcp.Options;

namespace Koan.Mcp;

/// <summary>
/// SEC-0014: the one exposure decision. <c>tools/list</c>, <c>tools/call</c>, the Explorer map and the Explorer try-it
/// ask it, so a surface that hides a tool also refuses to run it.
/// </summary>
public static class McpExposure
{
    /// <summary>
    /// The active mode: <see cref="McpServerOptions.Exposure"/> when set and not <c>Auto</c>, else the first assembly
    /// <see cref="McpDefaultsAttribute"/>, else <c>Full</c> (<c>Auto</c> is treated as <c>Full</c> until client
    /// capability detection exists).
    /// </summary>
    public static McpExposureMode Resolve(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Exposure is { } configured && configured != McpExposureMode.Auto) return configured;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var declared = assembly.GetCustomAttributes(typeof(McpDefaultsAttribute), false)
                .OfType<McpDefaultsAttribute>()
                .FirstOrDefault();
            if (declared?.ExposureMode is { } mode) return mode;
        }

        return McpExposureMode.Full;
    }

    /// <summary>Entity tools and custom <c>[McpTool]</c> verbs are listed and callable.</summary>
    public static bool ExposesTools(McpExposureMode mode) => mode is McpExposureMode.Tools or McpExposureMode.Full;

    /// <summary>The <c>koan_code_*</c> tools are listed and callable.</summary>
    public static bool ExposesCode(McpExposureMode mode) => mode is McpExposureMode.Code or McpExposureMode.Full;
}
