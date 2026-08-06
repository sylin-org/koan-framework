namespace Koan.Data.Abstractions;

/// <summary>A provider-neutral logical/physical value conversion used identically by reads, writes, and predicates.</summary>
public interface IDataMappingCodec
{
    string Id { get; }
    Type LogicalType { get; }
    Type PhysicalType { get; }
    bool CanEncode { get; }
    bool CanDecode { get; }
    object? Encode(object? logical);
    object? Decode(object? physical);
}
