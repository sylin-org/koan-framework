using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Couchbase;
using Couchbase.Diagnostics;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Data.Connector.Couchbase;

internal sealed class CouchbaseHealthContributor(IOptions<CouchbaseOptions> options, CouchbaseClusterProvider provider) : IHealthContributor
{
    public string Name => "data:couchbase";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var context = await provider.GetCollectionContextAsync(options.Value.Collection ?? string.Empty, ct).ConfigureAwait(false);
            await context.Cluster.PingAsync(new PingOptions().CancellationToken(ct)).ConfigureAwait(false);
            return new HealthReport(Name, HealthState.Healthy, null, null, new Dictionary<string, object?>
            {
                ["bucket"] = context.BucketName,
                ["scope"] = context.ScopeName,
                ["collection"] = context.CollectionName,
                ["connectionString"] = Redaction.DeIdentify(options.Value.ConnectionString)
            });
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, null, null);
        }
    }
}

