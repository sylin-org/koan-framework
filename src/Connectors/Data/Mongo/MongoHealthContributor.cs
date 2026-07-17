using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core.Observability.Health;
using Koan.Data.Core;
using Koan.Data.Core.Diagnostics;
using Koan.Data.Core.Routing;

namespace Koan.Data.Connector.Mongo;

/// <summary>Reports readiness for the Mongo sources that actually participate in this application.</summary>
internal sealed class MongoHealthContributor : DataAdapterHealthContributorBase
{
    private const string ProviderName = "mongo";
    private readonly IServiceProvider _services;
    private readonly MongoAdapterFactory _factory;

    public MongoHealthContributor(
        IServiceProvider services,
        IDataDiagnostics diagnostics,
        DataProviderCatalog providers,
        DataDefaultProviderPlan defaultProvider)
        : base(ProviderName, services, diagnostics, defaultProvider)
    {
        _services = services;
        _factory = providers.Find(ProviderName) as MongoAdapterFactory
            ?? throw new InvalidOperationException("The MongoDB provider is absent from the host Data catalog.");
    }

    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = _factory.ResolveRoute(_services, source);
        var database = await route.Provider.GetDatabase(ct).ConfigureAwait(false);
        await database.RunCommandAsync(
            (Command<BsonDocument>)new BsonDocument("ping", 1),
            cancellationToken: ct).ConfigureAwait(false);
    }
}
