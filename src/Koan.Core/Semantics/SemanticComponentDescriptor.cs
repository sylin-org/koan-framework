using System.Collections.Immutable;
using Koan.Core.Semantics.Contributions;

namespace Koan.Core.Semantics;

/// <summary>
/// Immutable, construction-free module availability metadata. Reading a descriptor never creates its
/// implementation; only the host-owned semantic module runtime may invoke <see cref="Factory"/>.
/// </summary>
internal sealed class SemanticComponentDescriptor
{
    public SemanticComponentDescriptor(
        string id,
        Type implementationType,
        Func<KoanModule> factory,
        IEnumerable<SemanticContributionBinding>? contributionBindings = null)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(factory);
        if (!typeof(KoanModule).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Semantic module implementation '{implementationType.FullName}' must derive from {nameof(KoanModule)}.",
                nameof(implementationType));
        }

        Id = new SemanticId(id);
        ImplementationType = implementationType;
        Factory = factory;
        ContributionBindings = Normalize(contributionBindings);
    }

    public SemanticId Id { get; }

    public Type ImplementationType { get; }

    public Func<KoanModule> Factory { get; }

    public ImmutableArray<SemanticContributionBinding> ContributionBindings { get; }

    public string? Version => ImplementationType.Assembly.GetName().Version?.ToString();

    public bool TryGetContribution(Type targetType, out SemanticContributionBinding binding)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        binding = ContributionBindings.FirstOrDefault(candidate => candidate.TargetType == targetType)!;
        return binding is not null;
    }

    private static ImmutableArray<SemanticContributionBinding> Normalize(
        IEnumerable<SemanticContributionBinding>? bindings)
    {
        if (bindings is null) return [];

        var ordered = bindings
            .Where(static binding => binding is not null)
            .OrderBy(static binding => binding.TargetType.AssemblyQualifiedName, StringComparer.Ordinal)
            .ToArray();
        var duplicate = ordered
            .GroupBy(static binding => binding.TargetType)
            .FirstOrDefault(static group => group.Skip(1).Any());
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Semantic module contribution target '{duplicate.Key.FullName}' was bound more than once.",
                nameof(bindings));
        }

        return ordered.ToImmutableArray();
    }
}
