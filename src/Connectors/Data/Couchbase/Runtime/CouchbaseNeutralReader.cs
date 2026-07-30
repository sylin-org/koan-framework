using Newtonsoft.Json.Linq;
using Koan.Data.Abstractions;

namespace Koan.Data.Connector.Couchbase.Runtime;

internal sealed class CouchbaseNeutralReader : INeutralRecordReader
{
    private readonly IReadOnlyList<DataRecord> _records;
    private int _next;

    private CouchbaseNeutralReader(IReadOnlyList<JObject> documents, NeutralRecordReaderCompletion completion)
    {
        Completion = completion;
        Fields = Shape(documents);
        _records = documents.Select(Record).ToArray();
    }

    public IReadOnlyList<DataField> Fields { get; }
    public NeutralRecordReaderCompletion Completion { get; }
    public bool HasAdditionalResultChannels => false;

    internal static CouchbaseNeutralReader Bounded(IReadOnlyList<JObject> documents, int take)
    {
        if (take <= 0) throw new ArgumentOutOfRangeException(nameof(take));
        var more = documents.Count > take;
        var visible = more ? documents.Take(take).ToArray() : documents;
        return new CouchbaseNeutralReader(
            visible,
            more ? NeutralRecordReaderCompletion.ProviderLimit : NeutralRecordReaderCompletion.Complete);
    }

    public ValueTask<DataRecord?> Read(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_next < _records.Count ? _records[_next++] : null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal static object? Neutral(JToken value) => value.Type switch
    {
        JTokenType.Null or JTokenType.Undefined => null,
        JTokenType.Object => new DataObject(((JObject)value).Properties()
            .Select(property => new DataProperty(property.Name, Neutral(property.Value)))),
        JTokenType.Array => new DataArray(((JArray)value).Select(Neutral)),
        _ when value is JValue scalar => Scalar(scalar),
        _ => throw new InvalidDataException($"Couchbase returned unsupported JSON token '{value.Type}'.")
    };

    private static object? Scalar(JValue value) => value.Value switch
    {
        Uri uri => uri.ToString(),
        char character => character.ToString(),
        _ => value.Value
    };

    private static IReadOnlyList<DataField> Shape(IReadOnlyList<JObject> documents)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
            foreach (var property in document.Properties())
                if (seen.Add(property.Name)) names.Add(property.Name);

        return names.Select((name, ordinal) =>
        {
            var values = documents
                .Select(document => document[name])
                .Where(static value => value is not null && value.Type != JTokenType.Null)
                .ToArray();
            var types = values.Select(static value => value!.Type.ToString()).Distinct().ToArray();
            var clr = values.Select(static value => Neutral(value!)?.GetType())
                .Where(static type => type is not null)
                .Distinct()
                .ToArray();
            return new DataField(
                ordinal,
                name,
                clr.Length == 1 ? clr[0] : null,
                types.Length == 1 ? types[0] : null,
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
            values[ordinal] = Neutral(token);
        }
        return new DataRecord(Fields, values, presence);
    }
}
