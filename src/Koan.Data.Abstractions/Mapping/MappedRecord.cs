namespace Koan.Data.Abstractions;

/// <summary>An immutable missing-preserving set of physical values plus its mapping receipt.</summary>
public sealed class MappedRecord
{
    private readonly Dictionary<PhysicalPath, MappedValue> _byPath;

    public MappedRecord(IEnumerable<MappedValue> values, MappingReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(receipt);
        var copy = values.ToArray();
        _byPath = new Dictionary<PhysicalPath, MappedValue>();
        foreach (var value in copy)
            if (!_byPath.TryAdd(value.Path, value))
                throw new ArgumentException($"Mapped record contains duplicate physical path '{value.Path}'.", nameof(values));
        Values = Array.AsReadOnly(copy);
        Receipt = receipt;
    }

    public IReadOnlyList<MappedValue> Values { get; }
    public MappingReceipt Receipt { get; }

    public bool TryGet(PhysicalPath path, out MappedValue value)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _byPath.TryGetValue(path, out value!);
    }
}
