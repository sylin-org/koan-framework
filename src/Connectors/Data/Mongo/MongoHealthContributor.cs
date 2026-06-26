using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// Health probe for Mongo connectivity — pings through the <b>shared</b> <see cref="MongoClientProvider"/> (no fresh
/// <c>MongoClient</c>/connection-pool per probe), on the <see cref="AdapterHealthContributorBase"/> family shape.
/// </summary>
internal sealed class MongoHealthContributor(MongoClientProvider provider, IOptions<MongoOptions> options)
    : AdapterHealthContributorBase
{
    public override string Name => "data:mongo";

    protected override async Task ProbeAsync(CancellationToken ct)
    {
        var db = await provider.GetDatabase(ct).ConfigureAwait(false);
        await db.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1), cancellationToken: ct).ConfigureAwait(false);
    }

    protected override IReadOnlyDictionary<string, object?>? HealthyData() => new Dictionary<string, object?>
    {
        ["database"] = options.Value.Database,
        ["connectionString"] = Redaction.DeIdentify(options.Value.ConnectionString),
    };
}
