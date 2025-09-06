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
using Sora.Data.Core;
using Sora.Flow;
using Sora.Flow.Attributes;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Flow.Options;
using Sora.Flow.Sending;
using Xunit;

namespace Sora.Flow.Core.Tests;

public sealed class SenderFlowTests
{
    public sealed class TestSenderModel : FlowEntity<TestSenderModel>
    {
        [AggregationKey] public string? Dummy { get; set; }
    }

    private static IHost? _host;
    private static async Task<IServiceProvider> CreateHostAsync(string workDir)
    {
        // ensure clean Data<> bindings
        if (_host is not null)
        {
            try { Sora.Core.Hosting.App.AppHost.Current = null; } catch { }
            try { await _host.StopAsync(TimeSpan.FromSeconds(1)); } catch { }
            try { _host.Dispose(); } catch { }
            _host = null;
        }
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
            o.BatchSize = 64;
            o.PurgeEnabled = false;
            o.ParkAndSweepEnabled = true;
        });
        var host = new HostBuilder().ConfigureServices(s => { foreach (var sd in sc) s.Add(sd); }).Build();
        await host.StartAsync();
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
        await predicate();
    }

    [Fact]
    public async Task Batch_Sender_Associate_Identity_Contractless()
    {
        var dir = NewWorkDir();
        using var _ = new TempDir(dir);
        var sp = await CreateHostAsync(dir);
        var sender = sp.GetRequiredService<IFlowSender>();
        var ct = CancellationToken.None;

    var payload = new FlowEvent()
            .With("dummy", "alpha") // aggregation tag value → owner key
            .WithAdapter("testsys", "testadp")
            .WithExternal("foo", "bar"); // contractless external id

        var item = new FlowSendItem(
            ModelType: typeof(TestSenderModel),
            SourceId: "sender-test",
            OccurredAt: DateTimeOffset.UtcNow,
            Payload: payload,
            CorrelationId: null
        );

        await sender.SendAsync(new[] { item }, ct);

        // Wait until KeyIndex created
        string? refUlid = null;
        await WaitUntilAsync(async () =>
        {
            var ki = await Data<KeyIndex<TestSenderModel>, string>.GetAsync("alpha", ct);
            if (ki is null) return false;
            refUlid = ki.ReferenceUlid;
            return !string.IsNullOrWhiteSpace(refUlid);
        }, TimeSpan.FromSeconds(6));

        refUlid.Should().NotBeNullOrWhiteSpace();

        // Identity link should exist for the external id
        var composite = string.Join('|', "testsys", "testadp", "bar");
        var link = await Data<IdentityLink<TestSenderModel>, string>.GetAsync(composite, ct);
        link.Should().NotBeNull();
        link!.ReferenceUlid.Should().Be(refUlid);

        // Canonical view should be produced (wait until available)
        var canId = $"{Constants.Views.Canonical}::{refUlid}";
        await WaitUntilAsync(async () =>
        {
            var c = await Data<CanonicalProjection<TestSenderModel>, string>.GetAsync(canId, FlowSets.ViewShort(Constants.Views.Canonical), ct);
            return c is not null;
        }, TimeSpan.FromSeconds(12));
        var can = await Data<CanonicalProjection<TestSenderModel>, string>.GetAsync(canId, FlowSets.ViewShort(Constants.Views.Canonical), ct);
        can.Should().NotBeNull();
        can!.ReferenceUlid.Should().Be(refUlid);
        can.ViewName.Should().Be(Constants.Views.Canonical);
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
