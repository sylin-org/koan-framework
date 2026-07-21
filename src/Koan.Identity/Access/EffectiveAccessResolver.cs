namespace Koan.Identity.Access;

/// <summary>
/// SEC-0007 Layer 2 — flattens every discovered <see cref="IEffectiveAccessContributor"/> into one effective-access
/// answer for an identity, and surfaces a role-overlap (role-explosion) early warning when the same role is conferred
/// by more than one source.
/// </summary>
public sealed class EffectiveAccessResolver
{
    private readonly IEnumerable<IEffectiveAccessContributor> _contributors;

    public EffectiveAccessResolver(IEnumerable<IEffectiveAccessContributor> contributors) => _contributors = contributors;

    public async Task<EffectiveAccess> ResolveAsync(string identityId, CancellationToken ct = default)
    {
        var facts = new List<AccessFact>();
        foreach (var contributor in _contributors)
            facts.AddRange(await contributor.ContributeAsync(identityId, ct).ConfigureAwait(false));

        var overlaps = facts
            .Where(f => f.Kind == "role")
            .GroupBy(f => f.Value)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        return new EffectiveAccess(identityId, facts, overlaps);
    }
}
