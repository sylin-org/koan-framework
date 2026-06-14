using System.Text.Json;
using Koan.Rag.Abstractions;

namespace Koan.Rag.Evaluation;

/// <summary>
/// Loads <see cref="RagTestSet"/> instances from external sources.
/// File I/O lives here in the implementation layer, not in the contracts layer.
/// </summary>
public static class RagTestSetLoader
{
    /// <summary>Load test cases from a JSON file (array of <see cref="RagTestCase"/>).</summary>
    public static async Task<RagTestSet> FromJsonFile(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("File path must be fully qualified", nameof(filePath));

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"RAG test set file not found: '{filePath}'", filePath);

        if (fileInfo.Length > 50 * 1024 * 1024) // 50 MB cap
            throw new InvalidOperationException(
                $"Test set file exceeds 50 MB limit ({fileInfo.Length / (1024 * 1024)} MB)");

        await using var stream = File.OpenRead(filePath);
        var cases = await JsonSerializer.DeserializeAsync<List<RagTestCase>>(stream, cancellationToken: ct);

        var testSet = new RagTestSet();
        if (cases is not null)
            testSet.AddRange(cases);
        return testSet;
    }
}
