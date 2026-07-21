using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// SEC-0005 (the Door) — a <c>[Door]</c> entity whose WRITE is gated by a capability scope. Read is open, so a
/// caller who lacks <c>showcase:write</c> sees the read verbs AND a <c>door</c> for write (named + how-to-unlock:
/// "requires scope:showcase:write") rather than a silent wall. Proves capability disclosure.
/// </summary>
[McpEntity(Name = "showcase", Exposure = McpExposureMode.Full)]
[Door]
[Access(write: "has:scope:showcase:write")]
[StorageName("conformance_showcases")]
public sealed class Showcase : Entity<Showcase>
{
    public string Title { get; set; } = "";
}

/// <summary>
/// SEC-0005 (the Door) — a <c>[Door]</c> entity whose REMOVE is gated by a ROLE (a privilege tier). Even with
/// <c>[Door]</c>, a role-gated verb is NEVER disclosed (09 §8 "admin is a Wall, not a Door" — disclosing it would
/// leak that a privileged capability exists). The remove must stay a silent Wall: absent from both verbs and doors.
/// </summary>
[McpEntity(Name = "vaultroom", Exposure = McpExposureMode.Full)]
[Door]
[Access(remove: "is:admin")]
[StorageName("conformance_vaultrooms")]
public sealed class VaultRoom : Entity<VaultRoom>
{
    public string Note { get; set; } = "";
}
