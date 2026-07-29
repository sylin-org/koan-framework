namespace Koan.Data.Vector.Connector.InMemory;

/// <summary>Host-owned memory and shape bounds for the exact in-process vector store.</summary>
public sealed class InMemoryVectorOptions
{
    public int MaxSpaces { get; set; } = Infrastructure.Constants.Defaults.MaxSpaces;
    public int MaxPointsPerSpace { get; set; } = Infrastructure.Constants.Defaults.MaxPointsPerSpace;
    public int MaxDimensions { get; set; } = Infrastructure.Constants.Defaults.MaxDimensions;
    public int MaxMetadataBytesPerPoint { get; set; } = Infrastructure.Constants.Defaults.MaxMetadataBytesPerPoint;
}
