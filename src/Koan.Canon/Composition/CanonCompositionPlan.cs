namespace Koan.Canon;

/// <summary>
/// Host-owned structural Canon decision shared by runtime, Web projection, startup, and facts.
/// </summary>
public sealed class CanonCompositionPlan
{
    private readonly IReadOnlyDictionary<Type, CanonModelPlan> _byType;

    internal CanonCompositionPlan(IReadOnlyList<CanonModelPlan> models)
    {
        Models = models ?? throw new ArgumentNullException(nameof(models));
        _byType = models.ToDictionary(static model => model.ModelType);
    }

    /// <summary>All discovered canonical Entity decisions in deterministic type-name order.</summary>
    public IReadOnlyList<CanonModelPlan> Models { get; }

    /// <summary>Returns the decision for a concrete canonical Entity type.</summary>
    public bool TryGetModel(Type modelType, out CanonModelPlan? model)
    {
        ArgumentNullException.ThrowIfNull(modelType);
        return _byType.TryGetValue(modelType, out model);
    }
}
