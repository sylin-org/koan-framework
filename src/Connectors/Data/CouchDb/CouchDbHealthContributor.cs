using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.CouchDb;

internal sealed class CouchDbHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly CouchDbAdapterFactory _factory;

    public CouchDbHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(Infrastructure.Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(Infrastructure.Constants.Provider) as CouchDbAdapterFactory
            ?? throw new InvalidOperationException("The CouchDB adapter is absent from the data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);
        var client = _services.GetRequiredService<Runtime.CouchDbClientManager>().Get(route);
        _ = await client.PingAsync(ct).ConfigureAwait(false);
    }
}
