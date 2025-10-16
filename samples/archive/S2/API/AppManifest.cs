using Koan.Orchestration.Abstractions;
using Koan.Orchestration.Attributes;

namespace S2.Api;

// Anchor for orchestration manifest (ARCH-0049): declares app metadata for Inspect/Planner
[KoanApp(
    AppCode = "s2api",
    AppName = "S2 API",
    Description = "Samples API",
    DefaultPublicPort = 8080,
    Capabilities = new[] { "http", "swagger", "graphql", "auth=oidc" }
)]
public sealed class AppManifest : IKoanManifest { }
