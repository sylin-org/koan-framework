namespace Koan.Core.Semantics.Segmentation;

/// <summary>Current runtime posture of one segmentation dimension.</summary>
public enum SegmentationValueKind
{
    Missing = 0,
    Concrete = 1,
    Host = 2
}
