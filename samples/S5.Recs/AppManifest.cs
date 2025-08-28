using Sora.Orchestration;
using Sora.Orchestration.Attributes;

namespace S5.Recs;

// App manifest anchor so Inspect surfaces app id/name/capabilities
[SoraApp(AppCode = "api", AppName = "S5 Recs API", Description = "Anime recommendations API", DefaultPublicPort = 8080,
    Capabilities = new[] { "http", "swagger", "graphql" })]
public sealed class AppManifest { }
