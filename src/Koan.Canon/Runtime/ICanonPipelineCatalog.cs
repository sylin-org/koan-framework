namespace Koan.Canon;

/// <summary>
/// Read-only compiled pipeline metadata consumed by Canon projections.
/// </summary>
public interface ICanonPipelineCatalog
{
    /// <summary>Returns compiled pipeline metadata for a canonical Entity type.</summary>
    bool TryGetMetadata(Type modelType, out CanonPipelineMetadata? metadata);
}
