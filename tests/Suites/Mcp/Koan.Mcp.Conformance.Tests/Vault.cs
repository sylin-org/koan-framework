using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>SEC-0004 Phase 3.3b — a scoped entity whose access is the data-layer <c>[Access]</c> gate (every verb
/// needs the <c>vault:read</c> scope), NOT the legacy <c>[McpEntity(RequiredScopes)]</c> transport filter. Used
/// to prove the gate denies an unscoped remote caller (and walls the whole entity in the catalog) while STDIO
/// (the raw handler, null principal) stays local-trust. <c>all:</c> gates read+write+remove uniformly, preserving
/// the old entity-wide behavior so an unscoped caller sees no verb at all (walled-means-silent).</summary>
[McpEntity(Name = "vault", Description = "A scoped vault", Exposure = McpExposureMode.Full)]
[Access(all: "has:scope:vault:read")]
[StorageName("conformance_vaults")]
public sealed class Vault : Entity<Vault>
{
    public string Secret { get; set; } = "";
}
