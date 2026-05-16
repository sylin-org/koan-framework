using System.Linq.Expressions;
using Koan.AI.Contracts.Shared;

namespace Koan.AI.Training;

/// <summary>
/// Internal service interface behind the Dataset.* static facade.
/// Resolved via DI. Implementations handle entity-to-dataset conversion,
/// file loading, and dataset analysis.
/// </summary>
public interface IDatasetService
{
    Task<DatasetRef> FromEntitiesAsync<T>(
        Expression<Func<T, bool>>? where,
        Expression<Func<T, string>> input,
        Expression<Func<T, string>> output,
        DataFormat format = DataFormat.Instruction,
        CancellationToken ct = default);

    Task<DatasetRef> FromFile(string path, CancellationToken ct = default);

    Task<DatasetAnalysis> Analyze(DatasetRef dataset, string? tokenizer = null, CancellationToken ct = default);
}
