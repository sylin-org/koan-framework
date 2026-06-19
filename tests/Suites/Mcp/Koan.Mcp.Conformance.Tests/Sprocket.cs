using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Mcp;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>
/// ARCH-0092 (Phase 1) — an entity with NO <c>[McpEntity]</c> and NO controller. Its ONLY path to MCP is
/// <see cref="SprocketToolset"/> — the explicit realization class, the symmetric peer of writing an
/// <c>EntityController</c>. Proves toolset discovery is a real second registration source and that a
/// toolset-only entity is fully functional over the shared governed endpoint service.
/// </summary>
[StorageName("an_sprockets")]
public sealed class Sprocket : Entity<Sprocket>
{
    public string Name { get; set; } = "";
}

public sealed class SprocketToolset : EntityToolset<Sprocket>
{
}
