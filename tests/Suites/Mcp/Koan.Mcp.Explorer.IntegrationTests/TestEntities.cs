using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;
using Koan.Web.Authorization;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>Public entity — no <c>[Access]</c>, so allow-by-default (anonymous can see + call its verbs).</summary>
[McpEntity(Name = "trinket", Description = "A public trinket.", Exposure = McpExposureMode.Full)]
[StorageName("explorer_trinkets")]
public sealed class Trinket : Entity<Trinket>
{
    public string Label { get; set; } = "";
}

/// <summary>Door entity — every verb needs the <c>docs:read</c> scope (a non-role gate) and the entity is
/// <c>[Door]</c>, so an unscoped caller sees a disclosed <c>door</c> (with "requires scope:docs:read"), not a
/// silent wall. With the scope, the doors become callable verbs.</summary>
[McpEntity(Name = "docvault", Description = "Docs behind a scope.", Exposure = McpExposureMode.Full)]
[Access(all: "has:scope:docs:read")]
[Door]
[StorageName("explorer_docvaults")]
public sealed class DocVault : Entity<DocVault>
{
    public string Title { get; set; } = "";
}

/// <summary>Wall entity — every verb is ROLE-gated (<c>admin</c>). Even though it is denied, a role gate is NEVER
/// disclosed ("admin is a Wall") — the entity is omitted entirely from a non-admin caller's surface.</summary>
[McpEntity(Name = "adminlog", Description = "Admin-only log.", Exposure = McpExposureMode.Full)]
[Access(all: "is:admin")]
[StorageName("explorer_adminlogs")]
public sealed class AdminLog : Entity<AdminLog>
{
    public string Message { get; set; } = "";
}
