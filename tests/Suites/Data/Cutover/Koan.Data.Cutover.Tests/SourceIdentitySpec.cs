using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Routing;
using Koan.Data.Cutover.Options;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Cutover.Tests;

/// <summary>
/// NK-060 and NK-178: a source configured through a provider-specific key used to get the connection identity
/// <c>none</c>, which it shared with every other such source. Each source is now minted once from its resolved physical
/// connection, and every consumer (gates, cutover, drift detection, Direct) uses that plan.
/// </summary>
public sealed class SourceIdentitySpec
{
    private const string Archive = "Archive";

    [Fact]
    public async Task Closing_the_default_for_maintenance_leaves_an_unrelated_source_open()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths, ProviderKeys(paths));
            AppHost.Current = host.Services;
            await using var maintenance = await CloseDefault(host);

            using (EntityContext.Source(Archive))
                await new CutoverRecord { Id = "archive", Value = "open" }.Save(TestContext.Current.CancellationToken);
            using (EntityContext.Source(Archive))
                (await CutoverRecord.Get("archive", TestContext.Current.CancellationToken)).Should().NotBeNull();
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task An_adapter_override_route_does_not_share_the_default_gate()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths, ProviderKeys(paths));
            AppHost.Current = host.Services;
            await using var maintenance = await CloseDefault(host);

            using (EntityContext.Adapter("inmemory"))
                await new CutoverRecord { Id = "memory", Value = "open" }.Save(TestContext.Current.CancellationToken);
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task A_cutover_between_two_databases_named_by_provider_keys_is_physically_distinct()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths, ProviderKeys(paths));
            AppHost.Current = host.Services;
            await new CutoverRecord { Id = "0001", Value = "source" }.Save(TestContext.Current.CancellationToken);

            var transition = Koan.Data.Core.Data.Source(Archive).PromoteToDefault();
            var plan = await transition.Plan(TestContext.Current.CancellationToken);

            plan.Blockers.Should().NotContain(blocker => blocker.Code == "koan.data.cutover.target-policy");
            plan.Source.ConnectionIdentity.Should().NotBe(plan.Target.ConnectionIdentity);
            plan.CanRun.Should().BeTrue();
            var receipt = await transition.Run(TestContext.Current.CancellationToken);
            receipt.Active.Source.Should().BeEquivalentTo(Archive);
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task A_nested_mutation_into_another_source_is_refused_as_a_multi_route_operation()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths, ProviderKeys(paths));
            AppHost.Current = host.Services;
            var authority = host.Services.GetRequiredService<DefaultDataRouteAuthority>();
            var horizon = host.Services.GetRequiredService<DataOperationHorizon>();
            await using var outer = await horizon.Enter(
                authority.Bind(authority.Current.Plan, DataRouteOrigin.Default),
                DataOperationEffect.Write,
                "outer mutation",
                TestContext.Current.CancellationToken);

            Func<Task> nested = async () =>
            {
                using (EntityContext.Source(Archive))
                    await new CutoverRecord { Id = "nested", Value = "nested" }.Save(TestContext.Current.CancellationToken);
            };

            await nested.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*expand an active mutation*multi-route*");
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task Changing_the_database_behind_a_provider_key_is_detected_as_drift()
    {
        var paths = TestPaths.Create();
        try
        {
            var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [$"Koan:Data:Sources:Default:sqlite:ConnectionString"] = $"Data Source={paths.Source};Pooling=False",
                [$"Koan:Data:Sources:{Archive}:Adapter"] = "sqlite",
                [$"Koan:Data:Sources:{Archive}:ConnectionString"] = $"Data Source={paths.Target};Pooling=False"
            };
            await using (var host = await Boot(paths, settings))
            {
                // A failed change persists the route record with Default still active.
                await FailChange(host);
                File.Exists(paths.State).Should().BeTrue();
                AppHost.Current = null;
                TestHooks.ResetDataConfigs();
            }

            settings["Koan:Data:Sources:Default:sqlite:ConnectionString"] = $"Data Source={paths.Other};Pooling=False";
            Func<Task> boot = async () => await (await Boot(paths, settings)).DisposeAsync();

            await boot.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*no longer matches its saved physical identity*");
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task A_maintenance_window_on_the_default_blocks_a_Direct_write_to_the_same_database()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = $"Data Source={paths.Source};Pooling=False"
            });
            AppHost.Current = host.Services;
            await using var maintenance = await CloseDefault(host);

            var data = host.Services.GetRequiredService<IDataService>();
            Func<Task> write = () => data.Direct(adapter: "sqlite")
                .Effect(DataOperationEffect.Write)
                .Execute("CREATE TABLE IF NOT EXISTS stray (id TEXT)", null, TestContext.Current.CancellationToken);

            await write.Should().ThrowAsync<DataSwitchInProgressException>();
            Func<Task> bySource = () => data.Direct(source: "Default")
                .Effect(DataOperationEffect.Write)
                .Execute("CREATE TABLE IF NOT EXISTS stray (id TEXT)", null, TestContext.Current.CancellationToken);
            await bySource.Should().ThrowAsync<DataSwitchInProgressException>();
        }
        finally
        {
            Reset(paths);
        }
    }

    [Fact]
    public async Task A_route_record_saved_with_the_legacy_none_identity_is_accepted_once_and_rewritten()
    {
        var paths = TestPaths.Create();
        try
        {
            var defaultRoute = DataSourceRegistry.IdentityOf("DEFAULT|SQLITE|none");
            var archiveRoute = DataSourceRegistry.IdentityOf("ARCHIVE|SQLITE|none");
            Directory.CreateDirectory(Path.GetDirectoryName(paths.State)!);
            await File.WriteAllTextAsync(paths.State, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["activeSource"] = "Default",
                ["adapter"] = "sqlite",
                ["routeIdentity"] = defaultRoute,
                ["connectionIdentity"] = "none",
                ["revision"] = 3,
                ["activatedAt"] = "2026-10-01T00:00:00+00:00",
                ["generations"] = new JsonObject { [defaultRoute] = 2, [archiveRoute] = 1 },
                ["quarantinedRoutes"] = new JsonArray(archiveRoute),
                ["transition"] = null
            }.ToJsonString(), TestContext.Current.CancellationToken);

            await using (var host = await Boot(paths, ProviderKeys(paths)))
            {
                var current = host.Services.GetRequiredService<DefaultDataRouteAuthority>().Current;
                var archive = host.Services.GetRequiredService<DataSourceRegistry>().GetPlan(Archive, "sqlite");
                current.Plan.ConnectionIdentity.Should().NotBe("none");
                current.AuthorityRevision.Should().Be(3);
                current.ContentGeneration.Should().Be(2);
                current.QuarantinedRouteIdentities.Should().Contain(archive.RouteIdentity);

                var saved = JsonNode.Parse(await File.ReadAllTextAsync(paths.State, TestContext.Current.CancellationToken))!;
                saved["connectionIdentity"]!.GetValue<string>().Should().Be(current.Plan.ConnectionIdentity);
                saved["routeIdentity"]!.GetValue<string>().Should().Be(current.Plan.RouteIdentity);
                saved["generations"]![current.Plan.RouteIdentity]!.GetValue<long>().Should().Be(2);
                saved["quarantinedRoutes"]!.AsArray().Select(node => node!.GetValue<string>())
                    .Should().ContainSingle().Which.Should().Be(archive.RouteIdentity);
                AppHost.Current = null;
                TestHooks.ResetDataConfigs();
            }

            await using var restarted = await Boot(paths, ProviderKeys(paths));
            restarted.Services.GetRequiredService<DefaultDataRouteAuthority>().Current.AuthorityRevision.Should().Be(3);
        }
        finally
        {
            Reset(paths);
        }
    }

    private static async Task<DataRouteMaintenanceWindow> CloseDefault(IntegrationHost host)
    {
        var authority = host.Services.GetRequiredService<DefaultDataRouteAuthority>();
        var binding = authority.Bind(authority.Current.Plan, DataRouteOrigin.Default);
        return await host.Services.GetRequiredService<DataOperationHorizon>().CloseAndDrain(
            [new DataRouteMaintenanceRequest(binding, BlockReads: false, BlockWrites: true)],
            TestContext.Current.CancellationToken);
    }

    private static async Task FailChange(IntegrationHost host)
    {
        var authority = host.Services.GetRequiredService<DefaultDataRouteAuthority>();
        var target = host.Services.GetRequiredService<DataSourceRegistry>().GetPlan(Archive, "sqlite");
        await using var change = await authority.BeginChange("drift-probe", target, TestContext.Current.CancellationToken);
        await change.MarkPending(TestContext.Current.CancellationToken);
    }

    private static Dictionary<string, string?> ProviderKeys(TestPaths paths) => new(StringComparer.Ordinal)
    {
        ["ConnectionStrings:Default"] = $"Data Source={paths.Source};Pooling=False",
        [$"Koan:Data:Sources:{Archive}:Adapter"] = "sqlite",
        [$"Koan:Data:Sources:{Archive}:sqlite:ConnectionString"] = $"Data Source={paths.Target};Pooling=False"
    };

    private static Task<IntegrationHost> Boot(TestPaths paths, IReadOnlyDictionary<string, string?> sources)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Route:StatePath"] = paths.State,
            ["Koan:Data:Cutover:WriterOwnership"] =
                CutoverWriterOwnership.HostExclusiveOrExternallyQuiesced.ToString()
        };
        foreach (var (key, value) in sources) settings[key] = value;
        return KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(static services => services.AddKoan())
            .StartAsync(TestContext.Current.CancellationToken);
    }

    private static void Reset(TestPaths paths)
    {
        AppHost.Current = null;
        TestHooks.ResetDataConfigs();
        paths.Delete();
    }

    private sealed record TestPaths(string Root, string Source, string Target, string Other, string State)
    {
        internal static TestPaths Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "koan-data-identity", Guid.CreateVersion7().ToString("n"));
            Directory.CreateDirectory(root);
            return new TestPaths(
                root,
                Path.Combine(root, "source.db"),
                Path.Combine(root, "archive.db"),
                Path.Combine(root, "other.db"),
                Path.Combine(root, "control", "active-route.json"));
        }

        internal void Delete()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }
}
