namespace Koan.Web.Auth.Standing;

/// <summary>
/// The composed answer to "may this subject act, and as what, now". Every <see cref="ISubjectStandingContributor"/> is
/// asked; any that says inactive decides and the rest are not read. Roles are the union of those reported. When two name
/// the subject differently, the first by contributor type name stands, so the result never rests on registration order.
/// </summary>
public sealed class SubjectStandings(IEnumerable<ISubjectStandingContributor> contributors)
{
    private readonly ISubjectStandingContributor[] _contributors = contributors
        .OrderBy(static contributor => contributor.GetType().FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>The standing of <paramref name="subject"/>, or null when no contributor knows them.</summary>
    public async Task<SubjectStanding?> Of(string subject, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        var known = false;
        string? name = null;
        HashSet<string>? roles = null;
        foreach (var contributor in _contributors)
        {
            if (await contributor.Read(subject, ct).ConfigureAwait(false) is not { } standing) continue;
            if (!standing.Active) return new SubjectStanding(false);
            known = true;
            name ??= standing.Name;
            if (standing.Roles is not null) (roles ??= new(StringComparer.Ordinal)).UnionWith(standing.Roles);
        }

        return known ? new SubjectStanding(true, name, roles) : null;
    }
}
