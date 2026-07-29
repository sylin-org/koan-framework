using Koan.Data.Connector.Couchbase.Runtime;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Couchbase;

internal sealed class CouchbaseHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly CouchbaseAdapterFactory _factory;
    private readonly CouchbaseResourcePool _resources;

    public CouchbaseHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider,
        CouchbaseResourcePool resources)
        : base(Infrastructure.Constants.Provider, services, diagnostics, defaultProvider)
    {
        _services = services;
        _resources = resources;
        _factory = providers.Find(Infrastructure.Constants.Provider) as CouchbaseAdapterFactory
            ?? throw new InvalidOperationException("The Couchbase adapter is absent from the data catalog.");
    }

    protected override Task ProbeSource(string source, CancellationToken ct) =>
        _resources.Probe(_factory.ResolveRoute(_services, source), ct);
}
