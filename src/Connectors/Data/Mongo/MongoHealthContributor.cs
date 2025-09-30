using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// Health probe for Mongo connectivity and database ping.
/// </summary>
internal sealed class MongoHealthContributor(IOptions<MongoOptions> options) : IHealthContributor
{
    public string Name => "data:mongo";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var client = new MongoClient(options.Value.ConnectionString);
            var db = client.GetDatabase(options.Value.Database);
            // ping
            await db.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1), cancellationToken: ct);
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Healthy, null, null, new Dictionary<string, object?>
            {
                ["database"] = options.Value.Database,
                ["connectionString"] = Redaction.DeIdentify(options.Value.ConnectionString)
            });
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, Koan.Core.Observability.Health.HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}
