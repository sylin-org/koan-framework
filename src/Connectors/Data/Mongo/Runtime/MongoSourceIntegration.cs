using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoSourceIntegration(MongoRoute route, MongoClientManager clients) : IDataSourceIntegration
{
    public SourceIntegrationCapabilities Capabilities =>
        SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar;

    public IDataSourceInspectorAdapter Inspector { get; } = new MongoInspector(route, clients);

    public bool Supports(IDataOperationBinding binding, OperationResultKind result) =>
        binding is MongoPipelineBinding && result is OperationResultKind.Records or OperationResultKind.Scalar;

    public bool EnforcesReadLane(DataReadLanePlan lane) => false;

    public async Task<INeutralRecordReader> ExecuteRecords(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        var collection = Collection(binding.Collection);
        var pipeline = Bind(plan, binding, parameters);
        pipeline.Add(new BsonDocument("$limit", (long)plan.Limits.MaxRecords + 1));
        var records = await collection.Aggregate<BsonDocument>(
                pipeline,
                new AggregateOptions { MaxTime = plan.Timeout },
                ct)
            .ToListAsync(ct).ConfigureAwait(false);
        return MongoNeutralReader.Bounded(records, plan.Limits.MaxRecords);
    }

    public async Task<SourceScalarResult> ExecuteScalar(
        OperationPlan plan,
        IReadOnlyList<BoundOperationParameter> parameters,
        CancellationToken ct = default)
    {
        var binding = Require(plan);
        var pipeline = Bind(plan, binding, parameters);
        pipeline.Add(new BsonDocument("$limit", 2));
        var records = await Collection(binding.Collection).Aggregate<BsonDocument>(
                pipeline,
                new AggregateOptions { MaxTime = plan.Timeout },
                ct)
            .ToListAsync(ct).ConfigureAwait(false);
        if (records.Count == 0) return new SourceScalarResult(0, 0, null);
        var document = records[0];
        var value = document.ElementCount == 1 ? MongoValues.ToNeutral(document.GetElement(0).Value) : null;
        var type = document.ElementCount == 1 ? document.GetElement(0).Value.BsonType.ToString() : null;
        return new SourceScalarResult(records.Count, document.ElementCount, value, type);
    }

    private IMongoCollection<BsonDocument> Collection(StorageAddress address)
    {
        MongoInspector.ValidateAddress(route, address);
        return clients.Database(route).GetCollection<BsonDocument>(address.Name);
    }

    private static MongoPipelineBinding Require(OperationPlan plan) =>
        plan.Binding as MongoPipelineBinding
        ?? throw new NotSupportedException($"MongoDB does not support registered binding '{plan.Binding.Kind}'.");

    private static List<BsonDocument> Bind(
        OperationPlan plan,
        MongoPipelineBinding binding,
        IReadOnlyList<BoundOperationParameter> parameters)
    {
        var values = parameters.ToDictionary(
            static parameter => parameter.Name,
            static parameter => MongoValues.FromNeutral(parameter.Value),
            StringComparer.OrdinalIgnoreCase);
        return binding.Parse().Select(stage => (BsonDocument)Replace(stage, plan, values)).ToList();
    }

    private static BsonValue Replace(
        BsonValue value,
        OperationPlan plan,
        IReadOnlyDictionary<string, BsonValue> parameters)
    {
        if (value is BsonString text &&
            text.Value.Length > 4 &&
            text.Value.StartsWith("{{", StringComparison.Ordinal) &&
            text.Value.EndsWith("}}", StringComparison.Ordinal))
        {
            var name = text.Value[2..^2].Trim();
            if (!parameters.TryGetValue(name, out var parameter))
                throw new OperationParameterException(
                    plan.Source,
                    plan.Name,
                    $"Pipeline placeholder '{{{{{name}}}}}' has no declared parameter.");
            return parameter.DeepClone();
        }
        if (value is BsonDocument document)
            return new BsonDocument(document.Select(element =>
                new BsonElement(element.Name, Replace(element.Value, plan, parameters))));
        if (value is BsonArray array)
            return new BsonArray(array.Select(item => Replace(item, plan, parameters)));
        return value.DeepClone();
    }
}
