using Koan.Data.Abstractions;
using MongoDB.Bson;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoNeutralReader : INeutralRecordReader
{
    private readonly IReadOnlyList<DataRecord> _records;
    private int _next;

    private MongoNeutralReader(
        IReadOnlyList<BsonDocument> documents,
        NeutralRecordReaderCompletion completion)
    {
        Completion = completion;
        Fields = Shape(documents);
        _records = documents.Select(Record).ToArray();
    }

    public IReadOnlyList<DataField> Fields { get; }
    public NeutralRecordReaderCompletion Completion { get; }
    public bool HasAdditionalResultChannels => false;

    public static MongoNeutralReader Bounded(IReadOnlyList<BsonDocument> documents, int take)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var more = documents.Count > take;
        var visible = more ? documents.Take(take).ToArray() : documents;
        return new MongoNeutralReader(
            visible,
            more ? NeutralRecordReaderCompletion.ProviderLimit : NeutralRecordReaderCompletion.Complete);
    }

    public ValueTask<DataRecord?> Read(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_next < _records.Count ? _records[_next++] : null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IReadOnlyList<DataField> Shape(IReadOnlyList<BsonDocument> documents)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
            foreach (var element in document)
                if (seen.Add(element.Name)) names.Add(element.Name);

        return names.Select((name, ordinal) =>
        {
            var values = documents
                .Where(document => document.TryGetValue(name, out _))
                .Select(document => document[name])
                .Where(static value => !value.IsBsonNull)
                .ToArray();
            var types = values.Select(static value => value.BsonType).Distinct().ToArray();
            var clr = values.Select(value => MongoValues.ToNeutral(value)?.GetType())
                .Where(static type => type is not null)
                .Distinct()
                .ToArray();
            return new DataField(
                ordinal,
                name,
                clr.Length == 1 ? clr[0] : null,
                types.Length == 1 ? types[0].ToString() : null,
                documents.Any(document => !document.TryGetValue(name, out var value) || value.IsBsonNull));
        }).ToArray();
    }

    private DataRecord Record(BsonDocument document)
    {
        var values = new object?[Fields.Count];
        var presence = new bool[Fields.Count];
        for (var ordinal = 0; ordinal < Fields.Count; ordinal++)
        {
            if (!document.TryGetValue(Fields[ordinal].Name, out var value)) continue;
            presence[ordinal] = true;
            values[ordinal] = MongoValues.ToNeutral(value);
        }
        return new DataRecord(Fields, values, presence);
    }
}
