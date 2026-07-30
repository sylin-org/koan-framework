namespace Koan.Data.Abstractions;

/// <summary>One record over a fixed shared field shape, retaining missing separately from null.</summary>
public sealed class DataRecord
{
    private readonly DataField[] _fields;
    private readonly object?[] _values;
    private readonly bool[] _present;

    public DataRecord(
        IReadOnlyList<DataField> fields,
        IReadOnlyList<object?> values,
        IReadOnlyList<bool>? presence = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(values);
        if (fields.Count != values.Count || (presence is not null && presence.Count != fields.Count))
            throw new ArgumentException("Field, value, and presence cardinality must match.");

        _fields = fields.ToArray();
        _values = new object?[values.Count];
        _present = new bool[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            _present[i] = presence is null || presence[i];
            if (_present[i]) _values[i] = NeutralDataValue.Normalize(values[i]);
        }
    }

    public int FieldCount => _fields.Length;

    public object? this[int ordinal] => TryGetValue(ordinal, out var value)
        ? value
        : throw new RecordValueMissingException(Field(ordinal));

    public object? this[string uniqueName]
    {
        get
        {
            var ordinal = UniqueOrdinal(uniqueName);
            return this[ordinal];
        }
    }

    public bool TryGetValue(int ordinal, out object? value)
    {
        _ = Field(ordinal);
        value = _values[ordinal];
        return _present[ordinal];
    }

    public bool TryGetValue(string uniqueName, out object? value)
    {
        var ordinals = FindOrdinals(uniqueName);
        if (ordinals.Count > 1) throw new RecordFieldAmbiguousException(uniqueName, ordinals);
        if (ordinals.Count == 0) { value = null; return false; }
        return TryGetValue(ordinals[0], out value);
    }

    public T Get<T>(int ordinal)
    {
        var field = Field(ordinal);
        if (!TryGetValue(ordinal, out var value)) throw new RecordValueMissingException(field);
        return (T?)NeutralDataValue.ConvertTo(value, typeof(T), field)!;
    }

    public T Get<T>(string uniqueName) => Get<T>(UniqueOrdinal(uniqueName));

    public IReadOnlyList<int> FindOrdinals(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var found = new List<int>();
        for (var i = 0; i < _fields.Length; i++)
            if (string.Equals(_fields[i].Name, name, StringComparison.Ordinal)) found.Add(i);
        return found;
    }

    internal DataField Field(int ordinal)
    {
        if ((uint)ordinal >= (uint)_fields.Length) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return _fields[ordinal];
    }

    internal IReadOnlyList<DataField> Shape => _fields;
    internal bool IsPresent(int ordinal) => _present[ordinal];

    private int UniqueOrdinal(string name)
    {
        var ordinals = FindOrdinals(name);
        if (ordinals.Count > 1) throw new RecordFieldAmbiguousException(name, ordinals);
        if (ordinals.Count == 0) throw new KeyNotFoundException($"Record field '{name}' does not exist.");
        return ordinals[0];
    }
}
