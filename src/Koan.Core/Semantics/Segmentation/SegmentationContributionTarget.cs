using System.ComponentModel;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>Framework/module-author target for declaring one hard segmentation dimension.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SegmentationContributionTarget
{
    private readonly SegmentationPlanBuilder _builder;
    private readonly string _owner;

    internal SegmentationContributionTarget(SegmentationPlanBuilder builder, string owner)
    {
        _builder = builder;
        _owner = owner;
    }

    public void Require(
        string id,
        Func<SegmentationValue> read,
        Func<Type, bool>? appliesTo,
        string correction) => _builder.Add(_owner, id, read, appliesTo, correction);
}
