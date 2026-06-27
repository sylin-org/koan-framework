namespace Koan.Identity.Access;

/// <summary>The flattened, human-answerable effective access for one identity — the "X can do A,B,C" view.</summary>
public sealed record EffectiveAccess(
    string IdentityId,
    IReadOnlyList<AccessFact> Facts,
    IReadOnlyList<string> OverlappingRoles)
{
    /// <summary>The distinct roles the identity holds (from any source).</summary>
    public IReadOnlyList<string> Roles =>
        Facts.Where(f => f.Kind == "role").Select(f => f.Value).Distinct().ToList();

    /// <summary>The distinct capability terms the identity holds.</summary>
    public IReadOnlyList<string> Capabilities =>
        Facts.Where(f => f.Kind == "capability").Select(f => f.Value).Distinct().ToList();
}
