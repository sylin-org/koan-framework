using Koan.Data.Abstractions;
using MongoDB.Bson;

namespace Koan.Data.Connector.Mongo
{
    /// <summary>A validated read-only MongoDB aggregation pipeline bound to one collection.</summary>
    public sealed class MongoPipelineBinding : IDataOperationBinding
    {
        private readonly string[] _stages;

        public MongoPipelineBinding(StorageAddress collection, IEnumerable<string> stages)
        {
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(stages);
            Collection = collection;
            _stages = stages.Select(Validate).ToArray();
        }

        public StorageAddress Collection { get; }
        public string Kind => "pipeline";
        public OperationBindingEffectProof EffectProof => OperationBindingEffectProof.ValidatedRead;

        internal IReadOnlyList<BsonDocument> Parse() =>
            _stages.Select(BsonDocument.Parse).ToArray();

        private static string Validate(string stage)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stage);
            BsonDocument parsed;
            try { parsed = BsonDocument.Parse(stage); }
            catch (Exception error) when (error is FormatException or BsonException)
            {
                throw new ArgumentException("A MongoDB pipeline stage must be valid extended JSON.", nameof(stage), error);
            }
            if (parsed.ElementCount != 1)
                throw new ArgumentException("A MongoDB pipeline stage must contain exactly one stage operator.", nameof(stage));
            var operation = parsed.GetElement(0).Name;
            if (operation is "$out" or "$merge")
                throw new ArgumentException($"MongoDB write stage '{operation}' is not allowed in a registered read pipeline.", nameof(stage));
            return parsed.ToJson();
        }
    }
}

namespace Koan.Data.Core
{
    /// <summary>Compact MongoDB binding leaves for registered read operations.</summary>
    public static class MongoOperationBuilderExtensions
    {
        public static RecordQueryBuilder Pipeline(
            this RecordQueryBuilder builder,
            string collection,
            params string[] stages)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(collection);
            return builder.Native(new Koan.Data.Connector.Mongo.MongoPipelineBinding(
                Koan.Data.Abstractions.StorageAddress.From(collection.Trim()),
                stages));
        }

        public static ScalarQueryBuilder Pipeline(
            this ScalarQueryBuilder builder,
            string collection,
            params string[] stages)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(collection);
            return builder.Native(new Koan.Data.Connector.Mongo.MongoPipelineBinding(
                Koan.Data.Abstractions.StorageAddress.From(collection.Trim()),
                stages));
        }
    }
}
