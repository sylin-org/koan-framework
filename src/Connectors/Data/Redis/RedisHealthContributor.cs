using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Redis;

internal sealed class RedisHealthContributor : DataAdapterHealthContributorBase
{
    private const string ProviderName = "redis";
    private readonly IServiceProvider _services;
    private readonly RedisAdapterFactory _factory;

    public RedisHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(ProviderName, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(ProviderName) as RedisAdapterFactory
            ?? throw new InvalidOperationException("The Redis provider is absent from the host Data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var route = _factory.ResolveRoute(_services, source);
        _ = await route.Connection.GetDatabase(route.Database).PingAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }
}

