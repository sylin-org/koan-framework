using Koan.Data.Abstractions;
using MongoDB.Bson;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoNeutralReader : INeutralRecordReader
{
    private readonly IReadOnlyList<DataRecord> _records;
    private readonly IReadOnlyList<(string Name, int Occurrence)> _slots;
    private int _next;

    private MongoNeutralReader(
        IReadOnlyList<BsonDocument> documents,
        NeutralRecordReaderCompletion completion)
    {
        Completion = completion;
        (Fields, _slots) = Shape(documents);
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

    private static (IReadOnlyList<DataField> Fields, IReadOnlyList<(string Name, int Occurrence)> Slots) Shape(
        IReadOnlyList<BsonDocument> documents)
    {
        var slots = new List<(string Name, int Occurrence)>();
        var seen = new HashSet<(string Name, int Occurrence)>();
        foreach (var document in documents)
        {
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var element in document)
            {
                occurrences.TryGetValue(element.Name, out var occurrence);
                occurrences[element.Name] = occurrence + 1;
                if (seen.Add((element.Name, occurrence))) slots.Add((element.Name, occurrence));
            }
        }

        var fields = slots.Select((slot, ordinal) =>
        {
            var values = new List<BsonValue>();
            var nullable = false;
            foreach (var document in documents)
            {
                if (!TryGetValue(document, slot, out var value))
                {
                    nullable = true;
                    continue;
                }
                if (value.IsBsonNull) nullable = true;
                else values.Add(value);
            }
            var types = values.Select(static value => value.BsonType).Distinct().ToArray();
            var clr = values.Select(value => MongoValues.ToNeutral(value)?.GetType())
                .Where(static type => type is not null)
                .Distinct()
                .ToArray();
            return new DataField(
                ordinal,
                slot.Name,
                clr.Length == 1 ? clr[0] : null,
                types.Length == 1 ? types[0].ToString() : null,
                nullable);
        }).ToArray();
        return (fields, slots);
    }

    private DataRecord Record(BsonDocument document)
    {
        var values = new object?[Fields.Count];
        var presence = new bool[Fields.Count];
        for (var ordinal = 0; ordinal < Fields.Count; ordinal++)
        {
            if (!TryGetValue(document, _slots[ordinal], out var value)) continue;
            presence[ordinal] = true;
            values[ordinal] = MongoValues.ToNeutral(value);
        }
        return new DataRecord(Fields, values, presence);
    }

    private static bool TryGetValue(
        BsonDocument document,
        (string Name, int Occurrence) slot,
        out BsonValue value)
    {
        var occurrence = 0;
        foreach (var element in document)
        {
            if (!string.Equals(element.Name, slot.Name, StringComparison.Ordinal)) continue;
            if (occurrence++ != slot.Occurrence) continue;
            value = element.Value;
            return true;
        }

        value = BsonNull.Value;
        return false;
    }
}
