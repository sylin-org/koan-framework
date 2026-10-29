namespace Koan.Identity.Access;

/// <summary>The flattened, human-answerable effective access for one identity — the "X can do A,B,C" view.</summary>
public sealed record EffectiveAccess(
    string IdentityId,
    IReadOnlyList<AccessFact> Facts,
    IReadOnlyList<string> OverlappingRoles)
{
    /// <summary>The distinct roles the identity holds (from any source).</summary>
    public IReadOnlyList<string> Roles =>
        Facts.Where(f => f.Kind == AccessFactKind.Role).Select(f => f.Value).Distinct().ToList();

    /// <summary>
    /// The distinct things the identity can do somewhere: agent-grant capability terms and role permissions, each
    /// fact naming the resource it holds on. A deny is not subtracted here; ask a resource with
    /// <see cref="AccessExplainer.Explain"/> or <see cref="Roles.RoleBag.On"/>.
    /// </summary>
    public IReadOnlyList<string> Capabilities =>
        Facts.Where(f => f.Kind is AccessFactKind.Capability or AccessFactKind.Permission).Select(f => f.Value).Distinct().ToList();
}
