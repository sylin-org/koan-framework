namespace Koan.Data.Abstractions;

public sealed class StorageContinuationSourceMismatchException : InvalidOperationException
{
    public StorageContinuationSourceMismatchException(string source)
        : base($"The continuation is not valid for source '{source}'. Re-list the selected source without a continuation.")
        => SourceName = source;

    public string SourceName { get; }
}
