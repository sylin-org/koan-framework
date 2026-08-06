namespace Koan.Data.Core;

public sealed class DataRuntimeOptions
{
    // If true, the runtime will attempt to ensure schemas exist for known entities on start.
    public bool EnsureSchemaOnStart { get; set; } = true;

    /// <summary>Maximum memoized healthy targets per source-readiness stage and host.</summary>
    public int ReadinessCacheEntries { get; set; } = 4096;

    /// <summary>Maximum observations retained in each host-owned Data diagnostics category.</summary>
    public int DiagnosticEntries { get; set; } = Infrastructure.Constants.Defaults.DiagnosticSourceEntries;

    /// <summary>Maximum restricted native evidence records retained by one host.</summary>
    public int NativeEvidenceEntries { get; set; } = Infrastructure.Constants.Defaults.NativeEvidenceEntries;

    /// <summary>Maximum storage anchors and physical names retained per category and host.</summary>
    public int StorageNameCacheEntries { get; set; } = Infrastructure.Constants.Defaults.StorageNameCacheEntries;

    /// <summary>Maximum named source declarations admitted while one host is composed.</summary>
    public int SourceEntries { get; set; } = Infrastructure.Constants.Defaults.SourceEntries;

    /// <summary>Maximum immutable named-source plans memoized by one host.</summary>
    public int SourcePlanEntries { get; set; } = Infrastructure.Constants.Defaults.SourcePlanEntries;

    /// <summary>Maximum root Entity repository routes admitted by one host.</summary>
    public int RepositoryEntries { get; set; } = Infrastructure.Constants.Defaults.RepositoryEntries;

    /// <summary>Maximum polymorphic Entity views admitted by one host.</summary>
    public int VariantRepositoryEntries { get; set; } = Infrastructure.Constants.Defaults.VariantRepositoryEntries;
}
