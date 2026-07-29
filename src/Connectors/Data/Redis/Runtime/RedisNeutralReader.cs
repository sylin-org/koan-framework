using Koan.Data.Abstractions;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Redis.Runtime;

internal sealed class RedisNeutralReader : INeutralRecordReader
{
    private readonly IReadOnlyList<DataRecord> _records;
    private int _next;

    private RedisNeutralReader(IReadOnlyList<JObject> documents, NeutralRecordReaderCompletion completion)
    {
        Completion = completion;
        Fields = Shape(documents);
        _records = documents.Select(Record).ToArray();
    }

    public IReadOnlyList<DataField> Fields { get; }
    public NeutralRecordReaderCompletion Completion { get; }
    public bool HasAdditionalResultChannels => false;

    internal static RedisNeutralReader Bounded(IReadOnlyList<JObject> documents, int maximum)
    {
        var additional = documents.Count > maximum;
        var visible = additional ? documents.Take(maximum).ToArray() : documents;
        return new RedisNeutralReader(
            visible,
            additional ? NeutralRecordReaderCompletion.ProviderLimit : NeutralRecordReaderCompletion.Complete);
    }

    public ValueTask<DataRecord?> Read(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_next < _records.Count ? _records[_next++] : null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IReadOnlyList<DataField> Shape(IReadOnlyList<JObject> documents)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
            foreach (var property in document.Properties())
                if (seen.Add(property.Name)) names.Add(property.Name);
        return names.Select((name, ordinal) =>
        {
            var values = documents.Select(document => document[name])
                .Where(static value => value is not null && value.Type != JTokenType.Null).ToArray();
            var clr = values.Select(static value => RedisJson.Neutral(value!)?.GetType())
                .Where(static type => type is not null).Distinct().ToArray();
            var native = values.Select(static value => value!.Type.ToString()).Distinct().ToArray();
            return new DataField(
                ordinal,
                name,
                clr.Length == 1 ? clr[0] : null,
                native.Length == 1 ? native[0] : null,
                documents.Any(document => document[name] is null or { Type: JTokenType.Null }));
        }).ToArray();
    }

    private DataRecord Record(JObject document)
    {
        var values = new object?[Fields.Count];
        var presence = new bool[Fields.Count];
        for (var ordinal = 0; ordinal < Fields.Count; ordinal++)
        {
            var token = document[Fields[ordinal].Name];
            if (token is null) continue;
            presence[ordinal] = true;
            values[ordinal] = RedisJson.Neutral(token);
        }
        return new DataRecord(Fields, values, presence);
    }
}
