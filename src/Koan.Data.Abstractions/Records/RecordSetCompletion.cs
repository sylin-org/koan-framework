namespace Koan.Data.Abstractions;

public enum RecordSetCompletion
{
    Complete,
    RecordLimit,
    ByteLimit,
    ValueLimit,
    DurationLimit,
    ProviderLimit
}
