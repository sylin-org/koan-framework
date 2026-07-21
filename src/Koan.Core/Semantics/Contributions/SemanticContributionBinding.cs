using System.ComponentModel;

namespace Koan.Core.Semantics.Contributions;

/// <summary>
/// Construction-free generated ABI binding one retained module to one exact contribution target.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SemanticContributionBinding
{
    public SemanticContributionBinding(
        Type targetType,
        Action<KoanModule, object> apply)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(apply);

        TargetType = targetType;
        Apply = apply;
    }

    public Type TargetType { get; }

    internal Action<KoanModule, object> Apply { get; }
}
