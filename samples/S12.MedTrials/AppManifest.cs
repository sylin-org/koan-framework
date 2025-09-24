using Koan.Orchestration;
using Koan.Orchestration.Attributes;

namespace S12.MedTrials;

[KoanApp(
    AppCode = "api",
    AppName = "S12 MedTrials API",
    Description = "Clinical trial operations copilot demonstrating AI + MCP integration",
    DefaultPublicPort = 5090,
    Capabilities = new[] { "http", "swagger" })]
public sealed class AppManifest { }
