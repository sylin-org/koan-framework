using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Vector;

public sealed class VectorDefaultsOptions
{
    public string? DefaultProvider { get; set; }
    public int RepositoryEntries { get; set; } = Infrastructure.Constants.Defaults.RepositoryEntries;
    public int MetadataShapeEntries { get; set; } = Infrastructure.Constants.Defaults.MetadataShapeEntries;
    public int MaxMetadataDepth { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataDepth;
    public int MaxTop { get; set; } = Infrastructure.Constants.Defaults.MaxTop;
}
