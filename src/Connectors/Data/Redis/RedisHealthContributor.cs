using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Redis;

internal sealed class RedisHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly RedisAdapterFactory _factory;

    public RedisHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Infrastructure.Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(Infrastructure.Constants.Provider) as RedisAdapterFactory
            ?? throw new InvalidOperationException("The Redis adapter is absent from the data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await _factory.ResolveRoute(_services, source).Data.PingAsync().WaitAsync(ct).ConfigureAwait(false);
    }
}
