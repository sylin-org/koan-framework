using Koan.Testing.Integration;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// One valid application composition for this project's broad direct-reference set. Every pillar spec exercises the
/// same host graph, so the directly referenced Local Storage connector receives one inert profile instead of forcing
/// unrelated tests to duplicate configuration or weakening its required options.
/// </summary>
internal static class PillarHost
{
    public static KoanIntegrationHost.Builder Configure() => KoanIntegrationHost.Configure()
        .WithSetting("Koan:Storage:DefaultProfile", "pillar-bootstrap")
        .WithSetting("Koan:Storage:Profiles:pillar-bootstrap:Provider", "local")
        .WithSetting("Koan:Storage:Profiles:pillar-bootstrap:Container", "pillar-bootstrap")
        .WithSetting("Koan:Storage:Providers:Local:BasePath", Path.GetTempPath());
}
