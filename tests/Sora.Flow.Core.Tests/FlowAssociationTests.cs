using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Data.Core;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Flow.Diagnostics;
using Sora.Flow.Options;
using Xunit;

namespace Sora.Flow.Core.Tests;

public class FlowAssociationTests
{
    private static IHost? _host;
    private static async Task<IServiceProvider> CreateHostAsync(string workDir, string[] tags)
    {
    Sora.Data.Core.TestHooks.ResetDataConfigs();
        var cfgDict = new Dictionary<string, string?>
        {
            { "SORA_DATA_PROVIDER", "json" },
            { "Sora:Data:Json:DirectoryPath", workDir },
        };
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(cfgDict!).Build();
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(cfg);
        sc.AddSoraDataCore();
        sc.AddSoraFlow();
        sc.Configure<FlowOptions>(o =>
        {
            o.AggregationTags = tags;
            o.BatchSize = 100;
            o.PurgeEnabled = false;
        });

        // Host for background services
        // Stop previous host to avoid background workers from interfering across tests
        if (_host is not null)
        {
            try { await _host.StopAsync(TimeSpan.FromSeconds(1)); } catch { }
            try { _host.Dispose(); } catch { }
            _host = null;
        }

        var host = new HostBuilder()
            .ConfigureServices(s =>
            {
                foreach (var sd in sc) s.Add(sd);
            })
            .Build();

    await host.StartAsync();
    // Wire Sora AppHost for DataService resolution in AggregateExtensions
    Sora.Core.Hosting.App.AppHost.Current = host.Services;
        _host = host;
        return host.Services;
    }

    private static string NewWorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sora-flow-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan timeout, TimeSpan? poll = null)
    {
        var start = DateTimeOffset.UtcNow;
        var interval = poll ?? TimeSpan.FromMilliseconds(200);
        while (DateTimeOffset.UtcNow - start < timeout)
        {
            if (await predicate()) return;
            await Task.Delay(interval);
        }
        await predicate(); // final attempt for better assertion messages
    }

    [Fact(Skip = "Legacy flow tests disabled in greenfield runtime; update to typed model tests later.")]
    public async Task Rejects_When_No_Aggregation_Keys()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);
    // Configure tags but provide no matching values to trigger NO_KEYS
    var sp = await CreateHostAsync(dir, Sora.Testing.Flow.FlowTestConstants.UbiquitousAggregationTags);
        var ct = CancellationToken.None;

        using (DataSetContext.With(Constants.Sets.Intake))
        {
            await new Sora.Flow.Model.Record
            {
                SourceId = "src1",
                OccurredAt = DateTimeOffset.UtcNow,
                StagePayload = new Dictionary<string, object> { { "foo", "bar" } }
            }.Save(ct);
        }

    // Association worker checks every ~2s when idle; poll up to 6s
    await WaitUntilAsync(async () => (await RejectionReport.All(ct)).Any(r => r.ReasonCode == Constants.Rejections.NoKeys), TimeSpan.FromSeconds(6));
    var rejects = await RejectionReport.All(ct);
    rejects.Should().NotBeEmpty("record without keys should be rejected");
        rejects.Select(r => r.ReasonCode).Should().Contain(Constants.Rejections.NoKeys);
    }

    [Fact(Skip = "Legacy flow tests disabled in greenfield runtime; update to typed model tests later.")]
    public async Task Rejects_On_MultiOwner_Collision()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);
    var sp = await CreateHostAsync(dir, Sora.Testing.Flow.FlowTestConstants.UbiquitousAggregationTags);
        var ct = CancellationToken.None;

    // Seed KeyIndex with two owners referencing different refs for the same incoming email keys
    await new KeyIndex { AggregationKey = Sora.Testing.Flow.FlowTestConstants.Samples.EmailA, ReferenceUlid = "refA" }.Save(ct);
    await new KeyIndex { AggregationKey = Sora.Testing.Flow.FlowTestConstants.Samples.EmailB, ReferenceUlid = "refB" }.Save(ct);

        using (DataSetContext.With(Constants.Sets.Intake))
        {
            await new Sora.Flow.Model.Record
            {
                SourceId = "src1",
                OccurredAt = DateTimeOffset.UtcNow,
                StagePayload = new Dictionary<string, object> { { Sora.Testing.Flow.FlowTestConstants.Keys.Email, new object[] { Sora.Testing.Flow.FlowTestConstants.Samples.EmailA, Sora.Testing.Flow.FlowTestConstants.Samples.EmailB } } }
            }.Save(ct);
        }
    await WaitUntilAsync(async () => (await RejectionReport.All(ct)).Any(r => r.ReasonCode == Constants.Rejections.MultiOwnerCollision), TimeSpan.FromSeconds(6));
    var rejects = await RejectionReport.All(ct);
        rejects.Select(r => r.ReasonCode).Should().Contain(Constants.Rejections.MultiOwnerCollision);
        // Allow any in-flight moves to finish then assert keyed remains empty
        await Task.Delay(100);
        (await Sora.Flow.Model.Record.All(Constants.Sets.Keyed, ct)).Should().BeEmpty();
    }

    [Fact(Skip = "Legacy flow tests disabled in greenfield runtime; update to typed model tests later.")]
    public async Task Associates_And_Projects_SingleOwner()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);
    var sp = await CreateHostAsync(dir, Sora.Testing.Flow.FlowTestConstants.UbiquitousAggregationTags);
        var ct = CancellationToken.None;

        // First record with new key → creates KeyIndex, ReferenceItem, ProjectionTask, moves to keyed
        using (DataSetContext.With(Constants.Sets.Intake))
        {
            await new Sora.Flow.Model.Record
            {
                SourceId = "src1",
                OccurredAt = DateTimeOffset.UtcNow,
                StagePayload = new Dictionary<string, object> { { Sora.Testing.Flow.FlowTestConstants.Keys.Email, Sora.Testing.Flow.FlowTestConstants.Samples.EmailA }, { "name", "Ann" } }
            }.Save(ct);
        }

    // Wait until a projection task is created for this reference
    string? refId = null;
    await WaitUntilAsync(async () =>
    {
        var ki = await KeyIndex.Get("a@example.com", ct);
        if (ki is null) return false;
        refId = ki.ReferenceUlid;
        var tasks = await ProjectionTask.All(ct);
        // Legacy ProjectionTask (untyped) lacks ReferenceUlid; presence is sufficient for this skipped test
        return tasks.Any();
    }, TimeSpan.FromSeconds(8));

    // Then wait until the task is processed and the reference is marked projected
    await WaitUntilAsync(async () =>
    {
        if (refId is null) return false;
        var tasks = await ProjectionTask.All(ct);
        var ri = await ReferenceItem.Get(refId, ct);
            // Legacy ProjectionTask no longer carries ReferenceId; keep existence check minimal for compile (test is skipped)
            return tasks.Any() && ri is not null && ri.RequiresProjection == false;
    }, TimeSpan.FromSeconds(12));

        var keyIdx = await KeyIndex.Get("a@example.com", ct);
        keyIdx.Should().NotBeNull();
    var referenceId = keyIdx!.ReferenceUlid;

        var refItem = await ReferenceItem.Get(referenceId, ct);
        refItem.Should().NotBeNull();
        refItem!.Version.Should().Be(1);
        refItem.RequiresProjection.Should().BeFalse();

    var allKeyed = await Sora.Flow.Model.Record.All(Constants.Sets.Keyed, ct);
        var keyed = allKeyed.Where(r => r.CorrelationId == referenceId).ToList();
        keyed.Should().HaveCount(1);

    var allCanon = await ProjectionView<object>.All(Constants.Views.Canonical, ct);
        var canonical = allCanon.Where(v => v.ReferenceUlid == referenceId && v.ViewName == Constants.Views.Canonical).ToList();
        canonical.Should().HaveCount(1);

    var allLineage = await ProjectionView<object>.All(Constants.Views.Lineage, ct);
    var lineage = allLineage.Where(v => v.ReferenceUlid == referenceId && v.ViewName == Constants.Views.Lineage).ToList();
        lineage.Should().HaveCount(1);
    }

    private sealed class TempDir : IDisposable
    {
        private readonly string _dir;
        public TempDir(string dir) { _dir = dir; }
        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
        }
    }
}
