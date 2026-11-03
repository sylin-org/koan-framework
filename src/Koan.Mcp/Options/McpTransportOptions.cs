using System;

namespace Koan.Mcp.Options;

public sealed class McpTransportOptions
{
    private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Interval used by transports to emit heartbeat diagnostics.
    /// </summary>
    public TimeSpan HeartbeatInterval
    {
        get => _heartbeatInterval;
        set => _heartbeatInterval = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : value;
    }

    /// <summary>
    /// Grace period granted to transports when shutting down.
    /// </summary>
    public TimeSpan ShutdownTimeout
    {
        get => _shutdownTimeout;
        set => _shutdownTimeout = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : value;
    }

    /// <summary>
    /// Logging category used by MCP transports when emitting structured diagnostics.
    /// </summary>
    public string LoggerCategory { get; set; } = "Koan.Transport.Mcp";
}
