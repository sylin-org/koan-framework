using System.Linq.Expressions;
using Koan.AI.Contracts.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.AI.Training;

/// <summary>
/// Static facade for dataset creation and analysis.
/// Converts entity data into training-ready datasets.
///
/// <code>
/// var dataset = await Dataset.From&lt;SupportTicket&gt;(
///     where: t => t.Resolved,
///     input: t => t.Question,
///     output: t => t.Answer);
///
/// var analysis = await Dataset.Analyze(dataset);
/// </code>
/// </summary>
public static class Dataset
{
    /// <summary>
    /// Create a dataset from entity data with input/output extraction.
    /// </summary>
    public static async Task<DatasetRef> From<T>(
        Expression<Func<T, bool>>? where,
        Expression<Func<T, string>> input,
        Expression<Func<T, string>> output,
        DataFormat format = DataFormat.Instruction,
        CancellationToken ct = default)
    {
        return await ResolveService().FromEntitiesAsync(where, input, output, format, ct);
    }

    /// <summary>Create a dataset from a local file (JSONL, CSV, Parquet).</summary>
    public static async Task<DatasetRef> From(
        string path,
        CancellationToken ct = default)
    {
        return await ResolveService().FromFileAsync(path, ct);
    }

    /// <summary>
    /// Load a dataset from multiple sources, merging them into a single reference.
    /// </summary>
    public static async Task<DatasetRef> Load(
        DatasetRef[] sources,
        CancellationToken ct = default)
    {
        // For now, return the first source as a merged reference.
        // Full implementation will merge datasets.
        if (sources.Length == 0)
            throw new ArgumentException("At least one source is required.", nameof(sources));

        _ = ct; // reserved for future async merge
        return await Task.FromResult(sources[0]);
    }

    /// <summary>Analyze a dataset: token counts, distribution, estimated training time.</summary>
    public static async Task<DatasetAnalysis> Analyze(
        DatasetRef dataset,
        string? tokenizer = null,
        CancellationToken ct = default)
    {
        return await ResolveService().AnalyzeAsync(dataset, tokenizer, ct);
    }

    // ── Internal ──

    private static IDatasetService ResolveService()
    {
        var provider = Koan.Core.Hosting.App.AppHost.Current
            ?? throw new InvalidOperationException(
                "Dataset service not configured; call services.AddKoan() and ensure " +
                "AppHost.Current is set during startup before using Dataset.*");

        return provider.GetRequiredService<IDatasetService>();
    }
}
