namespace Koan.Canon;

/// <summary>
/// Immutable structural decision for one discovered canonical Entity.
/// </summary>
public sealed class CanonModelPlan
{
    internal CanonModelPlan(Type modelType, IReadOnlyList<Type> contributorTypes)
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        ContributorTypes = contributorTypes ?? throw new ArgumentNullException(nameof(contributorTypes));
    }

    /// <summary>The concrete canonical Entity type.</summary>
    public Type ModelType { get; }

    /// <summary>The discovered custom contributors bound to this model.</summary>
    public IReadOnlyList<Type> ContributorTypes { get; }

    /// <summary>Whether the model has application-supplied contributors in addition to Canon defaults.</summary>
    public bool HasCustomContributors => ContributorTypes.Count > 0;
}
