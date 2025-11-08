namespace Koan.Context.Services;

/// <summary>
/// Exception thrown when a file exceeds the maximum size limit for indexing
/// </summary>
/// <remarks>
/// This is a non-critical exception - files that exceed the size limit are skipped
/// during indexing but should not fail the entire indexing job
/// </remarks>
public class FileSizeExceededException : Exception
{
    public string FilePath { get; }
    public double FileSizeMB { get; }
    public double MaxSizeMB { get; }

    public FileSizeExceededException(
        string message,
        string filePath,
        double fileSizeMB,
        double maxSizeMB)
        : base(message)
    {
        FilePath = filePath;
        FileSizeMB = fileSizeMB;
        MaxSizeMB = maxSizeMB;
    }
}
