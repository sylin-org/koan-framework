using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Couchbase;

/// <summary>Reports readiness only for Couchbase sources that participate in this application.</summary>
internal sealed class CouchbaseHealthContributor : DataAdapterHealthContributorBase
{
    private const string ProviderName = Infrastructure.Constants.Provider.Name;
    private readonly IServiceProvider _services;
    private readonly CouchbaseAdapterFactory _factory;

    public CouchbaseHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(ProviderName, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(ProviderName) as CouchbaseAdapterFactory
            ?? throw new InvalidOperationException("The Couchbase provider is absent from the host Data catalog.");
    }

    protected override Task ProbeSource(string source, CancellationToken ct)
        => _factory.ResolveRoute(_services, source).Provider.Probe(ct);
}

