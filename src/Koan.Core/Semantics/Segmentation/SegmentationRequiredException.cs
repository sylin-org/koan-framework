namespace Koan.Core.Semantics.Segmentation;

/// <summary>A hard segmentation dimension could not be bound before physical work began.</summary>
public sealed class SegmentationRequiredException : InvalidOperationException
{
    internal SegmentationRequiredException(string dimensionId, string operation, string correction)
        : base($"{operation} requires isolation context '{dimensionId}', but none is available. {correction}")
    {
        DimensionId = dimensionId;
        Operation = operation;
        Correction = correction;
    }

    public string DimensionId { get; }

    public string Operation { get; }

    public string Correction { get; }
}
