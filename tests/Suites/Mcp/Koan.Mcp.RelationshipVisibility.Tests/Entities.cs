using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;
using Koan.Mcp;

namespace Koan.Mcp.RelationshipVisibility.Tests;

/// <summary>
/// AN-leak (MCP coverage) — the parent/target type, exposed over MCP. A Secret maker is hidden from
/// anonymous callers; the MCP read path (which rides the same <c>IEntityEndpointService</c>) must omit
/// it from an expanded graph just like REST.
/// </summary>
[McpEntity(Name = "maker", Description = "A maker of works")]
[StorageName("mcp_an_makers")]
public sealed class Maker : Entity<Maker>
{
    public string Name { get; set; } = "";

    public bool Secret { get; set; }
}

/// <summary>
/// AN-leak (MCP coverage) — the child type with two divergent edges to <see cref="Maker"/>. Non-Published
/// works are walled for anonymous MCP callers and must never tunnel out through <c>maker.get-by-id</c>
/// with <c>with: "all"</c>.
/// </summary>
[McpEntity(Name = "work", Description = "A work")]
[StorageName("mcp_an_works")]
public sealed class Work : Entity<Work>
{
    public string Title { get; set; } = "";

    public WorkStatus Status { get; set; }

    [Parent(typeof(Maker))]
    public string? AuthorId { get; set; }

    [Parent(typeof(Maker))]
    public string? ReviewerId { get; set; }
}

public enum WorkStatus
{
    Draft = 0,
    Published = 1,
}
