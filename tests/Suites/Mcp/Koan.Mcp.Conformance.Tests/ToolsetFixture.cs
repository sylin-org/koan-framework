using Koan.Mcp.TestKit;

namespace Koan.Mcp.Conformance.Tests;

/// <summary>Boots Koan + MCP + Web with NO entity controllers registered — so the only way <see cref="Sprocket"/>
/// reaches MCP is its <see cref="SprocketToolset"/> (ARCH-0092 Phase 1, toolset discovery).</summary>
public sealed class ToolsetFixture : McpHarnessFixtureBase
{
}
