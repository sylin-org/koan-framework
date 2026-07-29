namespace Koan.Data.Abstractions;

public sealed class StorageReferenceSourceMismatchException : InvalidOperationException
{
    public StorageReferenceSourceMismatchException(string expectedSource, string actualSource)
        : base($"The storage reference belongs to source '{actualSource}', not '{expectedSource}'. Resolve it from the selected source.")
    {
        ExpectedSource = expectedSource;
        ActualSource = actualSource;
    }

    public string ExpectedSource { get; }
    public string ActualSource { get; }
}
