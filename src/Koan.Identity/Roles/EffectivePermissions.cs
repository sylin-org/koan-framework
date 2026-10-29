using Koan.Identity.Access;

namespace Koan.Identity.Roles;

/// <summary>
/// The permissions one bag holds on one resource, resolved in Discord's order: the bag's permissions — its member
/// roles' and the implicit roles' — then the <see cref="RoleTokens.Everyone"/> overwrite's deny and allow, then the
/// union of the denies and the union of the allows of the overwrites for the other roles the bag is in, then the
/// person's own overwrite's deny and allow. Within a step a deny is applied before an allow, so a later step and an
/// allow both win. A bag granted <see cref="RoleTokens.Administrator"/> holds every permission and no overwrite
/// applies. Built by <see cref="RoleBag.On"/>; it holds no I/O and no clock.
/// </summary>
public sealed class EffectivePermissions
{
    private readonly HashSet<string> _held;
    private readonly IReadOnlyDictionary<string, PermissionOverwrite> _deniedBy;
    private readonly IReadOnlyDictionary<string, PermissionOverwrite> _allowedBy;

    private EffectivePermissions(string resourceId, string? personId, bool administrator, HashSet<string> held,
        IReadOnlyDictionary<string, PermissionOverwrite> deniedBy, IReadOnlyDictionary<string, PermissionOverwrite> allowedBy)
    {
        ResourceId = resourceId;
        PersonId = personId;
        IsAdministrator = administrator;
        _held = held;
        _deniedBy = deniedBy;
        _allowedBy = allowedBy;
        Permissions = held.Order(StringComparer.Ordinal).ToArray();
    }

    public string ResourceId { get; }
    public string? PersonId { get; }

    /// <summary>The bag is granted <see cref="RoleTokens.Administrator"/>: every permission is held here.</summary>
    public bool IsAdministrator { get; }

    /// <summary>The permissions held on the resource. For an administrator, the ones its roles name; it holds all.</summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>Whether <paramref name="permission"/>, a <c>global:*</c> token, is held on the resource.</summary>
    public bool Holds(string permission) => HoldsNormalized(RoleContract.RequirePermission(permission, nameof(permission)));

    /// <summary>
    /// Why <paramref name="permission"/> is or is not held, in the explainer's shape. When it is missing, the one
    /// contributing fact is the deciding one: the deny of the most specific step that withheld it with no allow at
    /// that step or after — a <see cref="AccessFactKind.Deny"/> fact whose row a revoke removes — or, when no
    /// overwrite withheld it, the <see cref="AccessFactKind.Ungranted"/> fact that no role the person holds grants
    /// it. When it is held through an overwrite, the fact is that overwrite's allow; when the bag grants it, there is
    /// no row to name.
    /// </summary>
    public AccessDecision Explain(string permission)
    {
        permission = RoleContract.RequirePermission(permission, nameof(permission));
        if (HoldsNormalized(permission))
            return new AccessDecision(true, null,
                !IsAdministrator && _allowedBy.TryGetValue(permission, out var allowed) ? [allowed.Fact(AccessFactKind.Permission, permission)] : []);
        if (_deniedBy.TryGetValue(permission, out var denied))
            return new AccessDecision(false, $"{denied.Describe()} is denied {permission} on '{ResourceId}'",
                [denied.Fact(AccessFactKind.Deny, permission)]);
        return new AccessDecision(false, $"no held role grants {permission}",
            [new AccessFact(nameof(Role), AccessFactKind.Ungranted, permission, ResourceId, AccessFactScope.Global,
                RowType: "", RowId: "", ExpiresAt: null, Subject: PersonId)]);
    }

    private bool HoldsNormalized(string permission) => IsAdministrator || _held.Contains(permission);

    internal static EffectivePermissions Resolve(RoleBag bag, string resourceId, IEnumerable<PermissionOverwrite> overwrites,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(bag);
        ArgumentNullException.ThrowIfNull(overwrites);
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        var held = new HashSet<string>(bag.Permissions, StringComparer.Ordinal);
        var deniedBy = new Dictionary<string, PermissionOverwrite>(StringComparer.Ordinal);
        var allowedBy = new Dictionary<string, PermissionOverwrite>(StringComparer.Ordinal);
        if (held.Contains(RoleTokens.Administrator))
            return new EffectivePermissions(resourceId, bag.PersonId, true, held, deniedBy, allowedBy);

        var applying = overwrites
            .Where(row => row is not null && StringComparer.Ordinal.Equals(row.ResourceId, resourceId) && row.AppliesAt(now))
            .OrderBy(row => row.SubjectId, StringComparer.Ordinal)
            .ToList();
        Step(applying.Where(row => row.SubjectKind == OverwriteSubjectKind.Role && row.SubjectId == RoleTokens.Everyone));
        Step(applying.Where(row => row.SubjectKind == OverwriteSubjectKind.Role && row.SubjectId != RoleTokens.Everyone &&
                                   bag.InRole(row.SubjectId)));
        if (bag.PersonId is { } person)
            Step(applying.Where(row => row.SubjectKind == OverwriteSubjectKind.Person && row.SubjectId == person));
        return new EffectivePermissions(resourceId, bag.PersonId, false, held, deniedBy, allowedBy);

        void Step(IEnumerable<PermissionOverwrite> step)
        {
            var rows = step.ToList();
            foreach (var (permission, row) in Union(rows, row => row.Deny))
            {
                held.Remove(permission);
                deniedBy[permission] = row;
                allowedBy.Remove(permission);
            }

            foreach (var (permission, row) in Union(rows, row => row.Allow))
            {
                held.Add(permission);
                allowedBy[permission] = row;
                deniedBy.Remove(permission);
            }
        }
    }

    /// <summary>A step's permissions, each with the first row (by subject) that names it. The administrator
    /// permission is never an overwrite's to grant or withhold.</summary>
    private static Dictionary<string, PermissionOverwrite> Union(IEnumerable<PermissionOverwrite> rows,
        Func<PermissionOverwrite, IEnumerable<string>> permissions)
    {
        var union = new Dictionary<string, PermissionOverwrite>(StringComparer.Ordinal);
        foreach (var row in rows)
        foreach (var permission in permissions(row) ?? [])
        {
            if (permission != RoleTokens.Administrator) union.TryAdd(permission, row);
        }

        return union;
    }
}
