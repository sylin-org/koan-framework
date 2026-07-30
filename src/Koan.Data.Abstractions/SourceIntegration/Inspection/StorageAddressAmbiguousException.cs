namespace Koan.Data.Abstractions;

public sealed class StorageAddressAmbiguousException : InvalidOperationException
{
    public StorageAddressAmbiguousException(StorageAddress address, IReadOnlyList<string> safeCandidates)
        : base($"Storage address '{address}' is ambiguous. Select one of: {string.Join(", ", safeCandidates)}.")
    {
        Address = address;
        SafeCandidates = safeCandidates;
    }

    public StorageAddress Address { get; }
    public IReadOnlyList<string> SafeCandidates { get; }
}
