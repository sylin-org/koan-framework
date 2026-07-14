using System;
using System.Threading.Tasks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 base for engine integration specs. Injects the assembly-shared container fixture
/// (<typeparamref name="TFixture"/>) and provides the three things every connector spec needs:
/// a Docker-absent skip guard, a real reflective <c>AddKoan()</c> boot bound to the ambient host
/// (ARCH-0079), and a per-call isolation partition lease (ARCH-0091 Decision 4).
/// </summary>
/// <remarks>
/// Specs run sequentially within an engine assembly because <see cref="AppHost.Current"/> is a
/// process-default ambient owned by each booted host. <see cref="BoundHost"/> delegates that
/// ownership to Koan's generic-host binder; engines parallelize across processes (each test project
/// is its own xUnit v3 executable).
/// </remarks>
public abstract class KoanDataSpec<TFixture> where TFixture : KoanContainerFixture
{
    protected TFixture Fixture { get; }
    protected ITestOutputHelper Output { get; }

    protected KoanDataSpec(TFixture fixture, ITestOutputHelper output)
    {
        Fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>Skip the test (native v3 dynamic skip) when the backing store is unavailable.</summary>
    protected void RequireBackingStore()
    {
        if (!Fixture.IsAvailable)
        {
            Assert.Skip(Fixture.Reason ?? $"{Fixture.Engine} backing store unavailable");
        }
    }

    /// <summary>
    /// Boot a real Koan host over the fixture's settings via the ARCH-0079 reflective host. Koan's
    /// generic-host binder owns the ambient <see cref="AppHost.Current"/> binding so the static
    /// <c>Entity&lt;T&gt;</c> API resolves. Disposing (via <c>await using</c>) stops the host and releases
    /// that owner-checked binding.
    /// </summary>
    protected Task<BoundHost> BootAsync() => BootAsync(null);

    /// <summary>
    /// Boot a real Koan host (as <see cref="BootAsync()"/>) and additionally apply <paramref name="configure"/> to the
    /// service collection AFTER <c>AddKoan()</c> — so a spec can register a fake contributor (e.g. an
    /// <c>IReadFilterContributor</c>, DATA-0106) into the real boot's DI without a bespoke fixture.
    /// </summary>
    protected async Task<BoundHost> BootAsync(Action<IServiceCollection>? configure)
    {
        var host = await KoanIntegrationHost.Configure()
            .WithSettings(Fixture.SettingsForBoot())
            .ConfigureServices(s => { s.AddKoan(); configure?.Invoke(s); })
            .StartAsync()
            .ConfigureAwait(false);
        return new BoundHost(host);
    }

    /// <summary>
    /// Boot a real Koan host (as <see cref="BootAsync()"/>) with <paramref name="extraSettings"/> merged OVER the
    /// fixture's settings — e.g. extra routed data sources for an AODB Database-mode conformance cell. Later keys win.
    /// </summary>
    protected async Task<BoundHost> BootAsync(
        IEnumerable<System.Collections.Generic.KeyValuePair<string, string?>> extraSettings,
        Action<IServiceCollection>? configure = null)
    {
        var settings = new System.Collections.Generic.Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var kv in Fixture.SettingsForBoot()) settings[kv.Key] = kv.Value;
        foreach (var kv in extraSettings) settings[kv.Key] = kv.Value;

        var host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            .ConfigureServices(s => { s.AddKoan(); configure?.Invoke(s); })
            .StartAsync()
            .ConfigureAwait(false);
        return new BoundHost(host);
    }

    /// <summary>A fresh, unique partition name for this spec (engine-prefixed, GUID v7) for data isolation.</summary>
    protected string NewPartition(string? label = null)
    {
        var suffix = label is null ? string.Empty : $"-{label}";
        return $"{Fixture.Engine}-{Guid.CreateVersion7():n}{suffix}";
    }

    /// <summary>Lease a partition as the ambient scope so the static <c>Entity&lt;T&gt;</c> API targets it.</summary>
    protected static IDisposable Lease(string partition) => EntityContext.Partition(partition);

    /// <summary>A reflective Koan host whose generic-host binder owns <see cref="AppHost.Current"/>.</summary>
    protected sealed class BoundHost : IAsyncDisposable
    {
        private readonly IntegrationHost _host;
        internal BoundHost(IntegrationHost host) => _host = host;

        public IServiceProvider Services => _host.Services;

        public async ValueTask DisposeAsync()
        {
            await _host.DisposeAsync().ConfigureAwait(false);
        }
    }
}
