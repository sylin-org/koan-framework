using Couchbase.Query;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseSourceIntegration(CouchbaseRoute route, CouchbaseResourcePool resources)
    : IDataSourceIntegration
{
    public SourceIntegrationCapabilities Capabilities =>
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar;

    public IDataSourceInspectorAdapter Inspector { get; } = new CouchbaseInspector(route, resources);

    public bool Supports(IDataOperationBinding binding, OperationResultKind result) =>
        binding is SqlOperationBinding && result is OperationResultKind.Records or OperationResultKind.Scalar;

    public bool EnforcesReadLane(DataReadLanePlan lane) => route.ReadLanes.ContainsKey(lane.Name);

    public async Task<INeutralRecordReader> ExecuteRecords(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        var target = await resources.Target(ReadRoute(plan), ct).ConfigureAwait(false);
        var result = await target.Cluster.QueryAsync<JObject>(
                binding.CommandText,
                Configure(new QueryOptions(), plan, parameters))
            .ConfigureAwait(false);
        var documents = new List<JObject>(plan.Limits.MaxRecords + 1);
        await foreach (var row in result.Rows.WithCancellation(ct).ConfigureAwait(false))
        {
            documents.Add(row);
            if (documents.Count > plan.Limits.MaxRecords) break;
        }
        return CouchbaseNeutralReader.Bounded(documents, plan.Limits.MaxRecords);
    }

    public async Task<SourceScalarResult> ExecuteScalar(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        var target = await resources.Target(ReadRoute(plan), ct).ConfigureAwait(false);
        var result = await target.Cluster.QueryAsync<JToken>(
                binding.CommandText,
                Configure(new QueryOptions(), plan, parameters))
            .ConfigureAwait(false);
        var rows = new List<JToken>(2);
        await foreach (var row in result.Rows.WithCancellation(ct).ConfigureAwait(false))
        {
            rows.Add(row.DeepClone());
            if (rows.Count == 2) break;
        }
        if (rows.Count == 0) return new SourceScalarResult(0, 0, null);
        var first = rows[0];
        if (first is JObject document)
        {
            var properties = document.Properties().ToArray();
            return new SourceScalarResult(
                rows.Count,
                properties.Length,
                properties.Length == 1 ? CouchbaseNeutralReader.Neutral(properties[0].Value) : null,
                properties.Length == 1 ? properties[0].Value.Type.ToString() : null);
        }
        return new SourceScalarResult(
            rows.Count,
            1,
            CouchbaseNeutralReader.Neutral(first),
            first.Type.ToString());
    }

    private CouchbaseRoute ReadRoute(OperationPlan plan)
    {
        var lane = plan.Lane ?? throw new InvalidOperationException(
            $"Couchbase operation '{plan.Name}' requires a provider-enforced read lane.");
        if (!route.ReadLanes.TryGetValue(lane.Name, out var connection))
            throw new InvalidOperationException(
                $"Read lane '{lane.Name}' is not configured for source '{plan.Source}'.");
        return route with { ConnectionString = connection };
    }

    private QueryOptions Configure(
        QueryOptions options,
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters)
    {
        options.Readonly(true)
            .ScanConsistency(QueryScanConsistency.RequestPlus)
            .Timeout(plan.Timeout);
        foreach (var parameter in parameters)
            options.Parameter(parameter.Name, parameter.Value ?? JValue.CreateNull());
        return options;
    }

    private static SqlOperationBinding Require(OperationPlan plan) => plan.Binding as SqlOperationBinding
        ?? throw new NotSupportedException($"Couchbase does not support registered binding '{plan.Binding.Kind}'.");
}
