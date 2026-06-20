namespace Koan.Mcp.Hosting;

/// <summary>Header names for the deprecated legacy HTTP+SSE transport shim (AI-0037).</summary>
internal static class HttpSseHeaders
{
    /// <summary>The legacy session id header (distinct from Streamable's <c>Mcp-Session-Id</c>).</summary>
    public const string SessionId = "X-Mcp-Session";
}
