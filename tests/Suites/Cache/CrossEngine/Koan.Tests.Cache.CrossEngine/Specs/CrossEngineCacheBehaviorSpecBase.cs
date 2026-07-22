using Koan.Cache.AdapterSurface.TestKit;
using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Cache.CrossEngine.Specs;

/// <summary>
/// Binds the reusable Cache adapter contract to a real reference-driven Koan host. Concrete
/// subclasses change only provider identity and provider-owned configuration.
/// </summary>
public abstract class CrossEngineCacheBehaviorSpecBase : CacheAdapterConformanceSpecs
{
    protected abstract string LocalProvider { get; }
    protected override string Provider => LocalProvider;

    protected virtual IEnumerable<(string Key, string Value)> ExtraSettings() => [];

    protected override async Task<(IServiceProvider Services, IAsyncDisposable Lifetime)> StartHostAsync(
        CancellationToken cancellationToken)
    {
        var builder = KoanIntegrationHost.Configure()
            .WithSetting("Koan:Cache:LocalProvider", LocalProvider);
        foreach (var (key, value) in ExtraSettings()) builder.WithSetting(key, value);

        var host = await builder
            .ConfigureServices(services => services.AddKoan())
            .StartAsync(cancellationToken)
            .ConfigureAwait(false);
        return (host.Services, host);
    }
}
