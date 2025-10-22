using System;

namespace Koan.Mcp;

/// <summary>
/// Defines how MCP capabilities are exposed to clients.
/// </summary>
public enum McpExposureMode
{
    /// <summary>
    /// Automatically detect client capabilities and adapt.
    /// Falls back to Full if detection unavailable.
    /// Best for: Production deployments, unknown client mix.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Expose code execution only (koan.code.execute).
    /// Best for: Modern LLM agents, token optimization.
    /// </summary>
    Code = 1,

    /// <summary>
    /// Expose traditional entity tools only (entity.operation).
    /// Best for: Legacy MCP clients, explicit tool enumeration.
    /// </summary>
    Tools = 2,

    /// <summary>
    /// Expose both code execution and entity tools.
    /// Best for: Development, migration, maximum compatibility.
    /// </summary>
    Full = 3
}
