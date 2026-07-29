using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;
using Koan.Data.Connector.Mongo.Infrastructure;
using Koan.Data.Connector.Mongo.Runtime;

namespace Koan.Data.Connector.Mongo;

internal sealed class MongoHealthContributor : DataAdapterHealthContributorBase
{
    private readonly IServiceProvider _services;
    private readonly MongoAdapterFactory _factory;
    private readonly MongoClientManager _clients;

    public MongoHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider,
        MongoClientManager clients)
        : base(Constants.Provider.Name, services, diagnostics, defaultProvider)
    {
        _services = services;
        _clients = clients;
        _factory = providers.Find(Constants.Provider.Name) as MongoAdapterFactory
            ?? throw new InvalidOperationException("The MongoDB provider is absent from the host Data catalog.");
    }

    protected override Task ProbeSource(string source, CancellationToken ct) =>
        _clients.Ping(_factory.ResolveRoute(_services, source), ct);
}
