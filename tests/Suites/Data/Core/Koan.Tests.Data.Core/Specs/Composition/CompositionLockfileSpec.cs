using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Runtime;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Koan.Core.Diagnostics;
using Koan.Data.Core.Infrastructure;

namespace Koan.Tests.Data.Core.Specs.Composition;

/// <summary>
/// P1.1 — ARCH-0079 integration: a real <c>AddKoan()</c> host produces a resolved composition twin
/// that names a known module and the resolved <c>data:default</c> election. The data pillar enriches
/// the twin through the <c>IKoanCompositionContributor</c> seam (Reference = Intent).
/// </summary>
public class CompositionLockfileSpec
{
    [Fact]
    public async Task Resolved_twin_contains_known_module_and_data_election()
    {
        var root = Path.Combine(Path.GetTempPath(), "Koan-Composition", Guid.CreateVersion7().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            await using var host = await KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Data:Json:DirectoryPath", root)
                .ConfigureServices(s => s.AddKoan())
                .StartAsync();

            AppHost.Current = host.Services;

            // Discover() collects provenance (the module roster) and writes the resolved twin.
            host.Services.GetRequiredService<IAppRuntime>().Discover();

            var resolved = KoanCompositionSnapshot.BuildFromServices(host.Services);

            resolved.Schema.Should().Be(1);
            resolved.Modules.Select(m => m.Id).Should().Contain("Koan.Data.Core");

            resolved.Elections.Should().NotBeNull();
            resolved.Elections!.Should().ContainKey("data:default");

            // Highest [ProviderPriority] among the referenced adapters wins: Sqlite(10) > Json(0) > InMemory(-100).
            var election = resolved.Elections["data:default"];
            election.Adapter.Should().Be("sqlite");
            election.Via.Should().Be("reference-priority");

            var registry = host.Services.GetRequiredService<DataSourceRegistry>();
            var runtimeDecision = AdapterResolver.ResolveDefault(host.Services, registry);
            runtimeDecision.Adapter.Should().Be(election.Adapter);
            runtimeDecision.Via.Should().Be(election.Via);

            var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
                .Single(item => item.Code == Constants.Diagnostics.Codes.AdapterSelected
                    && item.Subject == "data:default");
            fact.State.Should().Be(KoanFactState.Selected);
            fact.ReasonCode.Should().Be(runtimeDecision.Via);
            fact.Summary.Should().Contain(runtimeDecision.Adapter);

            // Exercise the actual write path against a content root with an obj/ folder, then read it
            // back as the operator/agent would — the resolved twin must be valid, parseable JSON.
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            KoanCompositionSnapshot.TryWriteResolvedTwin(resolved, root);
            var twinPath = Path.Combine(root, "obj", "koan.lock.resolved.json");
            File.Exists(twinPath).Should().BeTrue();
            var twinJson = File.ReadAllText(twinPath);
            twinJson.Should().Contain("data:default").And.Contain("sqlite");
            KoanLockfileSerializer.Deserialize(twinJson)!.Elections!.Should().ContainKey("data:default");
        }
        finally
        {
            AppHost.Current = null;
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Unavailable_configured_default_is_rejected_with_a_safe_correction()
    {
        await using var host = await KoanIntegrationHost.Configure()
            .WithSetting("Koan:Environment", "Test")
            .WithSetting("Koan:Data:Sources:Default:Adapter", "not-referenced")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        AppHost.Current = host.Services;
        try
        {
            host.Services.GetRequiredService<IAppRuntime>().Discover();

            var fact = host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts
                .Single(item => item.Code == Constants.Diagnostics.Codes.AdapterRejected
                    && item.Subject == "data:default");

            fact.State.Should().Be(KoanFactState.Rejected);
            fact.ReasonCode.Should().Be(Constants.Diagnostics.Reasons.AdapterUnavailable);
            fact.Correction.Should().Contain("Koan:Data:Sources:Default:Adapter")
                .And.Contain("reference the connector package")
                .And.NotContain("ConnectionString");

            var action = () => KoanCompositionSnapshot.BuildFromServices(host.Services);
            action.Should().NotThrow("fact collection must describe the rejection without breaking inspection");
        }
        finally
        {
            AppHost.Current = null;
        }
    }
}
