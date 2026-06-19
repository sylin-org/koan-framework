using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0004 Phase 3.3 — a DATA-LAYER-gated MCP entity: read requires the <c>coffer:read</c> scope, declared with
/// the entity <c>[Access]</c> gate (NOT the legacy <c>[McpEntity(RequiredScopes)]</c> transport filter). It proves
/// that once the MCP caller's principal is threaded into <c>EntityRequestContext.User</c>, the unified gate enforces
/// on the MCP surface exactly as it does on REST — allow with the scope, deny without, deny anonymous.
/// </summary>
[McpEntity(Name = "coffer", Exposure = McpExposureMode.Full)]
[Access(read: "has:scope:coffer:read")]
[StorageName("conformance_coffers")]
public sealed class Coffer : Entity<Coffer>
{
    public string Contents { get; set; } = "";
}
