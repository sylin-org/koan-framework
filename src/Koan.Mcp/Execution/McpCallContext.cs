using System.Security.Claims;

namespace Koan.Mcp.Execution;

/// <summary>
/// SEC-0004 Phase 3.3 — the per-scope MCP caller principal. The code-mode executor resolves the sandbox SDK through
/// DI, so a constructor parameter cannot carry a per-call principal; instead the executor sets this once at the
/// execution-scope boundary and the SDK's entity proxy reads it and threads it EXPLICITLY into
/// <c>EndpointToolExecutor.Execute</c>. Scoped and set-once — this is NOT an ambient <c>AsyncLocal</c>; it lives and
/// dies with the single code-execution scope, so there is no cross-call leakage.
/// </summary>
public sealed class McpCallContext
{
    /// <summary>The caller's principal for this scope (null = anonymous).</summary>
    public ClaimsPrincipal? Principal { get; set; }
}
