namespace Koan.Identity.Roles;

public static class RoleTokens
{
    public const string Everyone = "everyone";
    public const string Authenticated = "authenticated";
    public const string GlobalPrefix = "global:";
    public const string RolePrefix = "role:";
    public const string GroupPrefix = "group:";

    /// <summary>
    /// The administrator-class permission. Resolved on a resource (<see cref="RoleBag.On"/>), a bag whose roles grant
    /// it holds every permission there and no overwrite applies to it, as Discord's ADMINISTRATOR. Only a role grants
    /// it; an overwrite can neither allow nor deny it. <see cref="Role.CanDo"/> intersects tokens as they are and does
    /// not expand it.
    /// </summary>
    public const string Administrator = "global:administrator";
}

public sealed class PermissionCriteria
{
    private PermissionCriteria(IEnumerable<string> tokens) => AnyOf = RoleContract.Normalize(tokens, 64, nameof(tokens));
    public IReadOnlyList<string> AnyOf { get; }
    public static PermissionCriteria Any(params string[] tokens) => new(tokens);
}

/// <summary>
/// What one person holds everywhere: the roles they are in and the <c>global:*</c> permissions those roles grant.
/// Every bag is in the <see cref="RoleTokens.Everyone"/> role and an authenticated bag is also in the
/// <see cref="RoleTokens.Authenticated"/> role, so the permissions of a role with either key reach the bag like a
/// member role's. <see cref="RoleCollection.Bag"/> compiles and caches it; an application or a test builds one with
/// the constructor to evaluate pure authorization over rows it has loaded itself.
/// </summary>
public sealed class RoleBag
{
    private readonly HashSet<string> _tokens;
    private readonly HashSet<string> _roles;

    /// <param name="personId">The person; required when <paramref name="authenticated"/>, ignored otherwise.</param>
    /// <param name="authenticated">Whether the bag is a signed-in person's.</param>
    /// <param name="roles">The role keys the person is a member of. The implicit roles are added.</param>
    /// <param name="permissions">The <c>global:*</c> permissions those roles grant; any other token is refused.</param>
    public RoleBag(string? personId, bool authenticated, IEnumerable<string> roles, IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);
        PersonId = authenticated ? RoleContract.Require(personId, nameof(personId)) : null;
        IsAuthenticated = authenticated;
        _roles = new(roles.Select(role => RoleContract.Require(role, nameof(roles))), StringComparer.Ordinal) { RoleTokens.Everyone };
        if (authenticated) _roles.Add(RoleTokens.Authenticated);
        var granted = permissions.Select(permission => RoleContract.RequirePermission(permission, nameof(permissions)))
            .ToHashSet(StringComparer.Ordinal);
        Roles = _roles.Order(StringComparer.Ordinal).ToArray();
        Permissions = granted.Order(StringComparer.Ordinal).ToArray();
        _tokens = new(_roles.Concat(granted), StringComparer.Ordinal);
        Tokens = _tokens.Order(StringComparer.Ordinal).ToArray();
    }

    public string? PersonId { get; }
    public bool IsAuthenticated { get; }

    /// <summary>The role keys the bag is in, the implicit <c>everyone</c> and <c>authenticated</c> included.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>The <c>global:*</c> permissions the bag's roles grant.</summary>
    public IReadOnlyList<string> Permissions { get; }

    /// <summary>The roles and permissions together: what <see cref="Role.CanDo"/> intersects.</summary>
    public IReadOnlyList<string> Tokens { get; }

    public bool Contains(string token) => _tokens.Contains(RoleContract.Require(token, nameof(token)));
    public bool ContainsAny(IEnumerable<string> tokens) => tokens.Any(_tokens.Contains);

    /// <summary>Whether the bag is in the role <paramref name="roleKey"/>, trimmed; false for a key that cannot name a role.</summary>
    public bool InRole(string? roleKey) => RoleContract.TryRequire(roleKey, out var key) && _roles.Contains(key);

    /// <summary>
    /// The bag's permissions on one resource after the resource's overwrites, resolved in Discord's order, with no
    /// I/O. <paramref name="overwrites"/> are rows the caller has loaded, usually <see cref="RoleCollection.Overwrites"/>;
    /// a row for another resource, for a role the bag is not in or for another person contributes nothing, and a row
    /// whose <see cref="PermissionOverwrite.ExpiresAt"/> is not after <paramref name="now"/> contributes nothing.
    /// </summary>
    public EffectivePermissions On(string resourceId, IEnumerable<PermissionOverwrite> overwrites, DateTimeOffset now)
        => EffectivePermissions.Resolve(this, resourceId, overwrites, now);
}

/// <summary>Application-neutral loaded-resource authorization seam.</summary>
public static class RoleResourceAuthorization
{
    public static bool CanDo<TResource>(TResource loadedResource, RoleBag personBag,
        Func<TResource, PermissionCriteria> criteria)
    {
        ArgumentNullException.ThrowIfNull(loadedResource);
        ArgumentNullException.ThrowIfNull(criteria);
        return Role.CanDo(criteria(loadedResource), personBag);
    }
}

public sealed class RoleOptions
{
    public const string SectionPath = "Koan:Identity:Roles";
    public int MaxCachedBags { get; set; } = 1024;
    public int MaxRolesPerPerson { get; set; } = 256;
    public int MaxMembersPerRole { get; set; } = 4096;

    /// <summary>The bound on a role's permissions, and on each of an overwrite's allow and deny lists.</summary>
    public int MaxPermissionsPerRole { get; set; } = 256;
    public int MaxMetadataEntries { get; set; } = 32;
    public int MaxPageSize { get; set; } = 100;

    /// <summary>The most overwrites one resource holds, across its subjects.</summary>
    public int MaxOverwritesPerResource { get; set; } = 256;

    /// <summary>The most overwrites one subject — a role or a person — holds, across resources.</summary>
    public int MaxOverwritesPerSubject { get; set; } = 4096;

    /// <summary>The most resources whose compiled overwrites are cached.</summary>
    public int MaxCachedResources { get; set; } = 1024;
}

public sealed record RolePage(IReadOnlyList<Role> Items, long TotalCount, int Page, int PageSize);

internal static class RoleContract
{
    public static RoleOptions Validate(RoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxCachedBags is < 1 or > 100_000 || options.MaxRolesPerPerson is < 1 or > 10_000 ||
            options.MaxMembersPerRole is < 1 or > 1_000_000 || options.MaxPermissionsPerRole is < 1 or > 10_000 ||
            options.MaxMetadataEntries is < 0 or > 1_000 || options.MaxPageSize is < 1 or > 1_000 ||
            options.MaxOverwritesPerResource is < 1 or > 10_000 || options.MaxOverwritesPerSubject is < 1 or > 1_000_000 ||
            options.MaxCachedResources is < 1 or > 100_000)
            throw new InvalidOperationException("Koan:Identity:Roles contains an invalid or unsafe bound.");
        return options;
    }

    public static string Require(string? value, string name, int max = 256)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result) || result.Length > max)
            throw new ArgumentException($"{name} is required and must be at most {max} characters.", name);
        return result;
    }

    /// <summary>The trimmed value when it is present and at most <paramref name="max"/> characters.</summary>
    public static bool TryRequire(string? value, out string result, int max = 256)
    {
        result = value?.Trim() ?? "";
        return result.Length > 0 && result.Length <= max;
    }

    /// <summary>The trimmed permission token when the value is one.</summary>
    public static bool TryRequirePermission(string? value, out string result)
        => TryRequire(value, out result) && result.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal);

    /// <summary>A permission token: a <c>global:*</c> token, as a role grants it.</summary>
    public static string RequirePermission(string? value, string name)
    {
        var result = Require(value, name);
        if (!result.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal))
            throw new ArgumentException($"'{result}' is not a permission: a permission is a '{RoleTokens.GlobalPrefix}' token.", name);
        return result;
    }

    public static bool IsImplicitRole(string roleKey)
        => roleKey is RoleTokens.Everyone or RoleTokens.Authenticated;

    public static string[] Normalize(IEnumerable<string>? values, int max, string name, bool allowEmpty = false)
    {
        var result = (values ?? []).Select(x => Require(x, name)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if ((!allowEmpty && result.Length == 0) || result.Length > max)
            throw new ArgumentException($"{name} must contain {(allowEmpty ? "at most" : "between 1 and")} {max} distinct values.", name);
        return result;
    }
}
