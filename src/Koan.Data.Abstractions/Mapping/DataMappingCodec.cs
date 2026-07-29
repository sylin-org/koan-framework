namespace Koan.Data.Abstractions;

/// <summary>A compact symmetric or explicitly one-way mapping codec.</summary>
public sealed class DataMappingCodec<TLogical, TPhysical> : IDataMappingCodec
{
    private readonly Func<TLogical?, TPhysical?>? _encode;
    private readonly Func<TPhysical?, TLogical?>? _decode;

    public DataMappingCodec(
        Func<TLogical?, TPhysical?>? encode,
        Func<TPhysical?, TLogical?>? decode,
        string? id = null)
    {
        if (encode is null && decode is null)
            throw new ArgumentException("A mapping codec must encode, decode, or both.");
        _encode = encode;
        _decode = decode;
        Id = string.IsNullOrWhiteSpace(id)
            ? $"{typeof(TLogical).FullName}->{typeof(TPhysical).FullName}"
            : id.Trim();
    }

    public string Id { get; }
    public Type LogicalType => typeof(TLogical);
    public Type PhysicalType => typeof(TPhysical);
    public bool CanEncode => _encode is not null;
    public bool CanDecode => _decode is not null;

    public object? Encode(object? logical)
    {
        if (_encode is null) throw new InvalidOperationException($"Mapping codec '{Id}' does not support encoding.");
        if (logical is not null && logical is not TLogical)
            throw new InvalidCastException($"Mapping codec '{Id}' expected logical type '{typeof(TLogical).FullName}'.");
        return _encode((TLogical?)logical);
    }

    public object? Decode(object? physical)
    {
        if (_decode is null) throw new InvalidOperationException($"Mapping codec '{Id}' does not support decoding.");
        if (physical is not null && physical is not TPhysical)
            throw new InvalidCastException($"Mapping codec '{Id}' expected physical type '{typeof(TPhysical).FullName}'.");
        return _decode((TPhysical?)physical);
    }
}
