using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace S12.MedTrials.McpService;

[KoanApp(
    AppCode = "mcp",
    AppName = "S12 MedTrials MCP Service",
    Description = "Dedicated MCP host exposing MedTrials tools over HTTP+SSE",
    DefaultPublicPort = 5114,
    Capabilities = new[] { "http", "mcp" })]
public sealed class AppManifest { }
