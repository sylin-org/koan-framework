namespace Koan.Data.SearchEngine;

/// <summary>
/// The option surface the shared <see cref="SearchEngineVectorRepository{TEntity,TKey}"/> reads. Both
/// the Elasticsearch and OpenSearch concrete option classes expose these (via the
/// <see cref="SearchEngineVectorOptions"/> base) so the repository is agnostic to which backend it
/// drives. Per-package binding concerns (<c>ConnectionString</c>, <c>Readiness</c>) stay on the
/// concrete classes — they are not read by the repository.
/// </summary>
public interface ISearchEngineVectorOptions
{
    string Endpoint { get; }
    string? ApiKey { get; }
    string? Username { get; }
    string? Password { get; }
    int DefaultTimeoutSeconds { get; }
    string RefreshMode { get; }
    string IdField { get; }
    string VectorField { get; }
    string MetadataField { get; }
    int? Dimension { get; }
    string? IndexName { get; }
    string? IndexPrefix { get; }
    bool DisableIndexAutoCreate { get; }
    string SimilarityMetric { get; }
}
