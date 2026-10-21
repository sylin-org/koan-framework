using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Routing;
using Koan.Data.Cutover.Options;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Koan.Data.Cutover.Tests;

public sealed class DefaultRouteTransitionSpec
{
    private const string Target = "LocalSqlite";

    [Fact]
    public async Task Verified_cutover_activates_target_invalidates_old_default_handles_and_survives_restart()
    {
        var paths = TestPaths.Create();
        try
        {
            await using (var host = await Boot(paths))
            {
                AppHost.Current = host.Services;
                await new CutoverRecord
                {
                    Id = "0001",
                    Value = "source-one",
                    ObservedAt = new DateTimeOffset(2026, 8, 6, 10, 0, 0, TimeSpan.Zero),
                    CorrelationId = Guid.Parse("f789e118-7612-48c8-85fa-e6cc9167de46"),
                    Evidence = [0, 1, 2, 255]
                }.Save(TestContext.Current.CancellationToken);
                await new CutoverRecord { Id = "0002", Value = "source-two" }.Save(TestContext.Current.CancellationToken);

                var oldDefault = host.Services.GetRequiredService<IDataService>()
                    .GetRepository<CutoverRecord, string>();
                var transition = Koan.Data.Core.Data.Source(Target).PromoteToDefault();
                var plan = await transition.Plan(TestContext.Current.CancellationToken);

                plan.CanRun.Should().BeTrue();
                plan.Entities.Should().ContainSingle(entity =>
                    entity.RootType == typeof(CutoverRecord).FullName &&
                    entity.Disposition == DefaultRouteEntityDisposition.Included);

                var receipt = await transition.Run(TestContext.Current.CancellationToken);

                receipt.Entities.Should().ContainSingle(entity => entity.Count == 2);
                receipt.Active.Source.Should().BeEquivalentTo(Target);
                (await CutoverRecord.Get("0001", TestContext.Current.CancellationToken))!
                    .Value.Should().Be("source-one");

                Func<Task> staleRead = async () =>
                    _ = await oldDefault.Get("0001", TestContext.Current.CancellationToken);
                await staleRead.Should().ThrowAsync<StaleDataRouteException>();

                using (EntityContext.Source("Default"))
                {
                    (await CutoverRecord.Get("0002", TestContext.Current.CancellationToken))!
                        .Value.Should().Be("source-two");
                }

                await new CutoverRecord { Id = "0003", Value = "target-only" }
                    .Save(TestContext.Current.CancellationToken);
                using (EntityContext.Source("Default"))
                    (await CutoverRecord.Get("0003", TestContext.Current.CancellationToken)).Should().BeNull();
                using (EntityContext.Source(Target))
                    (await CutoverRecord.Get("0003", TestContext.Current.CancellationToken))!
                        .Value.Should().Be("target-only");

                var active = host.Services.GetRequiredService<DefaultDataRouteAuthority>().Current;
                active.Plan.Source.Should().BeEquivalentTo(Target);
                active.AuthorityRevision.Should().Be(1);
                active.ContentGeneration.Should().Be(1);
                File.Exists(paths.State).Should().BeTrue();

                AppHost.Current = null;
                TestHooks.ResetDataConfigs();
            }

            await using var restarted = await Boot(paths);
            AppHost.Current = restarted.Services;
            var hydrated = restarted.Services.GetRequiredService<DefaultDataRouteAuthority>().Current;
            hydrated.Plan.Source.Should().BeEquivalentTo(Target);
            hydrated.AuthorityRevision.Should().Be(1);
            (await CutoverRecord.Get("0003", TestContext.Current.CancellationToken))!
                .Value.Should().Be("target-only");
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    /// <summary>
    /// NK-112. After a cutover an explicit EntityContext.Source("Default") still reaches the retired store, so a scheduled
    /// Move().From(source: "Default").To() upserted retired rows over the active store, reverting edits made since, and
    /// then deleted them. A transfer endpoint naming Default after a cutover is refused before it reads or writes.
    /// </summary>
    [Fact]
    public async Task After_a_cutover_a_transfer_naming_Default_is_refused_and_the_active_store_is_untouched()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths);
            AppHost.Current = host.Services;
            await new CutoverRecord { Id = "x", Value = "before cutover" }.Save(TestContext.Current.CancellationToken);
            await Koan.Data.Core.Data.Source(Target).PromoteToDefault().Run(TestContext.Current.CancellationToken);
            await new CutoverRecord { Id = "x", Value = "edited after cutover" }.Save(TestContext.Current.CancellationToken);

            var revert = async () => await CutoverRecord.Move().From(source: "Default").To().Run(TestContext.Current.CancellationToken);
            (await revert.Should().ThrowAsync<InvalidOperationException>())
                .WithMessage("*names source 'Default' in From()*retired store*");
            var archive = async () => await CutoverRecord.Copy().To(source: "default", partition: "archive")
                .Run(TestContext.Current.CancellationToken);
            (await archive.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*in To()*");

            (await CutoverRecord.Get("x", TestContext.Current.CancellationToken))!.Value.Should().Be("edited after cutover");
            using (EntityContext.Source("Default"))
                (await CutoverRecord.Get("x", TestContext.Current.CancellationToken))!.Value.Should().Be("before cutover");
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    /// <summary>
    /// NK-170. Each committed cutover made the next default-routed operation cache a new repository and kept the stale
    /// one, so a host hit 'Entity repository cache reached its configured limit' after enough cutovers and failed every
    /// first use until restart. A new default-derived repository replaces the stale one it supersedes.
    /// </summary>
    [Fact]
    public async Task Repository_cache_does_not_grow_with_committed_cutovers()
    {
        var paths = TestPaths.Create();
        var second = Path.Combine(paths.Root, "second.db");
        try
        {
            await using var host = await Boot(paths, services => services.PostConfigure<DataRuntimeOptions>(
                options => options.RepositoryEntries = 2), new Dictionary<string, string?>
            {
                ["Koan:Data:Sources:Second:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Second:ConnectionString"] = $"Data Source={second};Pooling=False",
                ["Koan:Data:Sources:Second:StorageLifecycle"] = "Managed",
                ["Koan:Data:Sources:Second:Access"] = "ReadWrite"
            });
            AppHost.Current = host.Services;
            await new CutoverRecord { Id = "a", Value = "first default" }.Save(TestContext.Current.CancellationToken);

            await Koan.Data.Core.Data.Source(Target).PromoteToDefault().Run(TestContext.Current.CancellationToken);
            await new CutoverRecord { Id = "b", Value = "second default" }.Save(TestContext.Current.CancellationToken);

            await Koan.Data.Core.Data.Source("Second").PromoteToDefault().Run(TestContext.Current.CancellationToken);
            await new CutoverRecord { Id = "c", Value = "third default" }.Save(TestContext.Current.CancellationToken);

            (await CutoverRecord.All(TestContext.Current.CancellationToken)).Select(record => record.Id)
                .Should().BeEquivalentTo(["a", "b", "c"]);
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    [Fact]
    public async Task Planning_rejects_a_nonempty_target_without_mutating_either_route()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths);
            AppHost.Current = host.Services;
            await new CutoverRecord { Id = "source", Value = "source" }.Save(TestContext.Current.CancellationToken);
            using (EntityContext.Source(Target))
                await new CutoverRecord { Id = "intruder", Value = "target" }
                    .Save(TestContext.Current.CancellationToken);

            var transition = Koan.Data.Core.Data.Source(Target).PromoteToDefault();
            var plan = await transition.Plan(TestContext.Current.CancellationToken);

            plan.CanRun.Should().BeFalse();
            plan.Blockers.Should().Contain(blocker => blocker.Code == "koan.data.cutover.target-not-empty");
            Func<Task> run = () => transition.Run(TestContext.Current.CancellationToken);
            await run.Should().ThrowAsync<DefaultRouteTransitionRejectedException>();
            host.Services.GetRequiredService<DefaultDataRouteAuthority>().Current.Plan.Source.Should().Be("Default");
            (await CutoverRecord.Get("source", TestContext.Current.CancellationToken)).Should().NotBeNull();
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    [Fact]
    public async Task Corrupt_durable_route_state_fails_host_start_instead_of_falling_back()
    {
        var paths = TestPaths.Create();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.State)!);
            await File.WriteAllTextAsync(paths.State, "{ not-valid-json", TestContext.Current.CancellationToken);

            Func<Task> boot = async () => await Boot(paths);
            await boot.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*durable Data route control record*");
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    [Fact]
    public async Task Failed_mutated_target_is_quarantined_durably_and_rejected_by_normal_Data_access()
    {
        var paths = TestPaths.Create();
        try
        {
            await using (var host = await Boot(paths))
            {
                AppHost.Current = host.Services;
                var authority = host.Services.GetRequiredService<DefaultDataRouteAuthority>();
                var registry = host.Services.GetRequiredService<DataSourceRegistry>();
                var target = registry.GetPlan(Target, "sqlite");

                await using (var change = await authority.BeginChange(
                                 "simulated-failure",
                                 target,
                                 TestContext.Current.CancellationToken))
                {
                    await change.MarkPending(TestContext.Current.CancellationToken);
                    await change.MarkTargetMutated(TestContext.Current.CancellationToken);
                }

                authority.Current.QuarantinedRouteIdentities.Should().Contain(target.RouteIdentity);
                using (EntityContext.Source(Target))
                {
                    Func<Task> write = () => new CutoverRecord { Id = "blocked", Value = "blocked" }
                        .Save(TestContext.Current.CancellationToken);
                    var failure = await write.Should().ThrowAsync<DataRouteUnavailableException>();
                    failure.Which.Code.Should().Be(DataRouteUnavailableException.QuarantinedCode);
                }

                AppHost.Current = null;
                TestHooks.ResetDataConfigs();
            }

            await using var restarted = await Boot(paths);
            AppHost.Current = restarted.Services;
            var hydrated = restarted.Services.GetRequiredService<DefaultDataRouteAuthority>();
            hydrated.Current.QuarantinedRouteIdentities.Should().NotBeEmpty();
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    [Fact]
    public async Task Maintenance_waits_for_admitted_mutation_and_fails_new_writes_with_retryable_switch_error()
    {
        var paths = TestPaths.Create();
        try
        {
            await using var host = await Boot(paths);
            AppHost.Current = host.Services;
            var authority = host.Services.GetRequiredService<DefaultDataRouteAuthority>();
            var horizon = host.Services.GetRequiredService<DataOperationHorizon>();
            var binding = authority.Bind(authority.Current.Plan, DataRouteOrigin.Default);
            var admitted = await horizon.Enter(
                binding,
                Koan.Data.Abstractions.Sources.DataOperationEffect.Write,
                "test admitted mutation",
                TestContext.Current.CancellationToken);

            var maintenanceTask = horizon.CloseAndDrain(
                [new DataRouteMaintenanceRequest(binding, BlockReads: false, BlockWrites: true)],
                TestContext.Current.CancellationToken);
            maintenanceTask.IsCompleted.Should().BeFalse();

            await admitted.DisposeAsync();
            await using var maintenance = await maintenanceTask;
            Func<Task> blocked = () => new CutoverRecord { Id = "closed", Value = "closed" }
                .Save(TestContext.Current.CancellationToken);
            var failure = await blocked.Should().ThrowAsync<DataSwitchInProgressException>();
            failure.Which.Code.Should().Be(DataSwitchInProgressException.FailureCode);

            await maintenance.DisposeAsync();
            await new CutoverRecord { Id = "reopened", Value = "reopened" }
                .Save(TestContext.Current.CancellationToken);
        }
        finally
        {
            AppHost.Current = null;
            TestHooks.ResetDataConfigs();
            paths.Delete();
        }
    }

    private static Task<IntegrationHost> Boot(
        TestPaths paths,
        Action<IServiceCollection>? configure = null,
        IReadOnlyDictionary<string, string?>? extra = null) => KoanIntegrationHost.Configure()
        .WithSettings(new Dictionary<string, string?>(extra ?? new Dictionary<string, string?>(), StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
            ["Koan:Data:Sources:Default:ConnectionString"] = $"Data Source={paths.Source};Pooling=False",
            [$"Koan:Data:Sources:{Target}:Adapter"] = "sqlite",
            [$"Koan:Data:Sources:{Target}:ConnectionString"] = $"Data Source={paths.Target};Pooling=False",
            [$"Koan:Data:Sources:{Target}:StorageLifecycle"] = "Managed",
            [$"Koan:Data:Sources:{Target}:Access"] = "ReadWrite",
            ["Koan:Data:Route:StatePath"] = paths.State,
            ["Koan:Data:Cutover:WriterOwnership"] =
                CutoverWriterOwnership.HostExclusiveOrExternallyQuiesced.ToString(),
            ["Koan:Data:Cutover:PageSize"] = "1"
        })
        .ConfigureServices(services =>
        {
            services.AddKoan();
            configure?.Invoke(services);
        })
        .StartAsync(TestContext.Current.CancellationToken);

    private sealed record TestPaths(string Root, string Source, string Target, string State)
    {
        internal static TestPaths Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "koan-data-cutover", Guid.CreateVersion7().ToString("n"));
            Directory.CreateDirectory(root);
            return new TestPaths(
                root,
                Path.Combine(root, "source.db"),
                Path.Combine(root, "target.db"),
                Path.Combine(root, "control", "active-route.json"));
        }

        internal void Delete()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }
}
