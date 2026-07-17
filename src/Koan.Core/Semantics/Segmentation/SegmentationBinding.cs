namespace Koan.Core.Semantics.Segmentation;

/// <summary>A concrete operation-bound dimension. Its value must not enter general facts or logs.</summary>
public readonly record struct SegmentationBinding(string DimensionId, string Value);
