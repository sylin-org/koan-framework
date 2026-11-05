namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0042 — what this application tells an MCP client in <c>initialize</c>'s <c>instructions</c>, composed when a client asks, so
/// it can say what the application is now and where it answers rather than a string fixed in configuration. Register one as a
/// singleton; with none, <c>Koan:Mcp:Instructions</c> and then the application's description are told.
/// </summary>
/// <example><code>services.AddSingleton&lt;IMcpInstructions, PlaceInstructions&gt;();</code></example>
public interface IMcpInstructions
{
    /// <summary>The instructions, or null to tell none. With a request in hand, it is the client's request to the edge.</summary>
    Task<string?> Instructions(CancellationToken ct);
}
