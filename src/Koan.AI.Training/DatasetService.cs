using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Default implementation of <see cref="IDatasetService"/>.
/// Converts entity queries and files into <see cref="DatasetRef"/> instances for
/// downstream consumption by the training pipeline.
/// </summary>
internal sealed class DatasetService : IDatasetService
{
    public Task<DatasetRef> FromEntitiesAsync<T>(
        Expression<Func<T, bool>>? where,
        Expression<Func<T, string>> input,
        Expression<Func<T, string>> output,
        DataFormat format = DataFormat.Instruction,
        CancellationToken ct = default)
    {
        _ = ct;

        // Compute a deterministic hash from the query shape so identical queries
        // always produce the same DatasetRef.
        var hash = ComputeQueryHash(typeof(T), where, input, output, format);
        var id = $"entity:{typeof(T).Name}:{hash[..12]}";

        // The DatasetRef is a lazy reference — actual data materialisation happens
        // when Training.Train() resolves the dataset. This avoids eager querying
        // and keeps dataset creation lightweight.
        return Task.FromResult(new DatasetRef(id, hash));
    }

    public Task<DatasetRef> FromFile(string path, CancellationToken ct = default)
    {
        _ = ct;

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path must not be empty.", nameof(path));

        if (!File.Exists(path))
            throw new FileNotFoundException($"Dataset file not found: {path}", path);

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var supportedExtensions = new[] { ".jsonl", ".csv", ".parquet", ".json", ".tsv" };

        if (Array.IndexOf(supportedExtensions, extension) < 0)
            throw new NotSupportedException(
                $"Unsupported dataset format '{extension}'. " +
                $"Supported formats: {string.Join(", ", supportedExtensions)}");

        var hash = ComputeFileHash(path);
        var id = $"file:{Path.GetFileNameWithoutExtension(path)}:{hash[..12]}";

        return Task.FromResult(new DatasetRef(id, hash));
    }

    public Task<DatasetAnalysis> Analyze(
        DatasetRef dataset,
        string? tokenizer = null,
        CancellationToken ct = default)
    {
        // Stub: full analysis requires tokenizer integration which ships with
        // Koan.AI.Training.Python or Koan.AI.Training.Container.
        _ = dataset;
        _ = tokenizer;
        _ = ct;

        return Task.FromResult(new DatasetAnalysis
        {
            TotalSamples = 0,
            AvgInputTokens = 0,
            AvgOutputTokens = 0,
            MaxInputTokens = 0,
            MaxOutputTokens = 0,
            EstimatedTrainTime = null
        });
    }

    // ── Hashing ──

    private static string ComputeQueryHash<T>(
        Type entityType,
        Expression<Func<T, bool>>? where,
        Expression<Func<T, string>> input,
        Expression<Func<T, string>> output,
        DataFormat format)
    {
        var sb = new StringBuilder();
        sb.Append(entityType.FullName);
        sb.Append('|');
        sb.Append(where?.ToString() ?? "<all>");
        sb.Append('|');
        sb.Append(input.ToString());
        sb.Append('|');
        sb.Append(output.ToString());
        sb.Append('|');
        sb.Append(format.ToString());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(bytes);
    }

    private static string ComputeFileHash(string path)
    {
        // Hash file path + last-write time for a lightweight identity.
        // Full content hashing happens during training materialisation.
        var info = new FileInfo(path);
        var input = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
