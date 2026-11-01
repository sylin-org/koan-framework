using System.Security.Cryptography;
using System.Text;

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
/// What one person holds everywhere: the roles they are in, the <c>global:*</c> permissions those roles grant, and the
/// position of the highest of them.
/// Every bag is in the <see cref="RoleTokens.Everyone"/> role and an authenticated bag is also in the
/// <see cref="RoleTokens.Authenticated"/> role, so the permissions of a role with either key reach the bag like a
/// member role's. <see cref="RoleCollection.Bag"/> compiles and caches it; an application or a test builds one with
/// the constructor to evaluate pure authorization over rows it has loaded itself.
/// </summary>
public sealed class RoleBag
{
    private readonly HashSet<string> _tokens;
    private readonly HashSet<string> _roles;
    private readonly HashSet<string> _permissions;

    /// <param name="personId">The person; required when <paramref name="authenticated"/>, ignored otherwise.</param>
    /// <param name="authenticated">Whether the bag is a signed-in person's.</param>
    /// <param name="roles">The role keys the person is a member of. The implicit roles are added.</param>
    /// <param name="permissions">The <c>global:*</c> permissions those roles grant; any other token is refused.</param>
    /// <param name="highest">The <see cref="Role.Position"/> of the highest of those roles; 0 when that is
    /// <see cref="RoleTokens.Everyone"/>.</param>
    public RoleBag(string? personId, bool authenticated, IEnumerable<string> roles, IEnumerable<string> permissions, int highest = 0)
    {
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentOutOfRangeException.ThrowIfNegative(highest);
        Highest = highest;
        PersonId = authenticated ? RoleContract.Require(personId, nameof(personId)) : null;
        IsAuthenticated = authenticated;
        _roles = new(roles.Select(role => RoleContract.Require(role, nameof(roles))), StringComparer.Ordinal) { RoleTokens.Everyone };
        if (authenticated) _roles.Add(RoleTokens.Authenticated);
        var granted = permissions.Select(permission => RoleContract.RequirePermission(permission, nameof(permissions)))
            .ToHashSet(StringComparer.Ordinal);
        _permissions = granted;
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

    /// <summary>
    /// The <see cref="Role.Position"/> of the highest role the bag is in: 0 when that is <see cref="RoleTokens.Everyone"/>.
    /// Of two bags, the one with the higher <see cref="Highest"/> stands above the other.
    /// </summary>
    public int Highest { get; }

    public bool Contains(string token) => _tokens.Contains(RoleContract.Require(token, nameof(token)));
    public bool ContainsAny(IEnumerable<string> tokens) => tokens.Any(_tokens.Contains);

    /// <summary>Whether the bag is in the role <paramref name="roleKey"/>, trimmed; false for a key that cannot name a role.</summary>
    public bool InRole(string? roleKey) => RoleContract.TryRequire(roleKey, out var key) && _roles.Contains(key);

    /// <summary>
    /// Whether the bag manages <paramref name="role"/> — the role and its members — and, given <paramref name="to"/>,
    /// may move it to that position: the role is below the bag's highest role (<see cref="Highest"/>), and so is
    /// <paramref name="to"/>. A bag whose roles grant <see cref="RoleTokens.Administrator"/> manages every role and moves
    /// it anywhere. Nobody manages their own highest role, and nobody moves <see cref="RoleTokens.Everyone"/>, which is
    /// always at 0. Pure, over the row the caller has read; <see cref="RoleCollection.Manages"/> reads it by key.
    /// </summary>
    public bool Manages(Role role, int? to = null)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (to is not null && role.Key == RoleTokens.Everyone) return false;
        if (InRole(role.Key) && role.Position >= Highest) return false;
        if (Administers) return true;
        return role.Position < Highest && (to is null || to < Highest);
    }

    /// <summary>Whether the bag's roles grant <see cref="RoleTokens.Administrator"/>.</summary>
    internal bool Administers => _permissions.Contains(RoleTokens.Administrator);

    /// <summary>
    /// The bag's permissions on one resource after the resource's overwrites, resolved in Discord's order, with no
    /// I/O. <paramref name="overwrites"/> are rows the caller has loaded, usually <see cref="RoleCollection.OverwritesFor"/>;
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

/// <summary>
/// The role model's configuration. The bounds a person can meet are properties of the model — the roles one person
/// holds, the permissions a role or an overwrite list names, a role's metadata — and a read is bounded by its page.
/// The cache capacities size memory: past one, an entry nothing has read lately is evicted, and nothing is refused.
/// </summary>
public sealed class RoleOptions
{
    public const string SectionPath = "Koan:Identity:Roles";

    /// <summary>The people whose compiled bags stay in memory.</summary>
    public int MaxCachedBags { get; set; } = 1024;

    /// <summary>The roles one person holds; adding a person to one more is refused.</summary>
    public int MaxRolesPerPerson { get; set; } = 256;

    /// <summary>The bound on a role's permissions, and on each of an overwrite's allow and deny lists.</summary>
    public int MaxPermissionsPerRole { get; set; } = 256;
    public int MaxMetadataEntries { get; set; } = 32;

    /// <summary>The most rows one page of a role, member or overwrite listing returns.</summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>The resources whose role overwrites stay compiled in memory.</summary>
    public int MaxCachedResources { get; set; } = 1024;

    /// <summary>The (resource, person) pairs whose own overwrite, or its absence, stays in memory.</summary>
    public int MaxCachedPersonOverwrites { get; set; } = 16_384;

    /// <summary>
    /// How long a hold's first role change waits for the role changes already under way before it refuses. It is short
    /// because such a change can be waiting for the store the hold keeps, and then only the hold's end lets it finish.
    /// </summary>
    public TimeSpan HeldChangeWait { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed record RolePage(IReadOnlyList<Role> Items, long TotalCount, int Page, int PageSize);

/// <summary>A page of a role's members in person order; <see cref="Next"/> continues it, and is null after the last.</summary>
public sealed record RoleMemberPage(IReadOnlyList<RoleMember> Items, string? Next);

/// <summary>A page of overwrites; <see cref="Next"/> continues it, and is null after the last.</summary>
public sealed record OverwritePage(IReadOnlyList<PermissionOverwrite> Items, string? Next);

internal static class RoleContract
{
    public static RoleOptions Validate(RoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxCachedBags is < 1 or > 100_000 || options.MaxRolesPerPerson is < 1 or > 10_000 ||
            options.MaxPermissionsPerRole is < 1 or > 10_000 || options.MaxMetadataEntries is < 0 or > 1_000 ||
            options.MaxPageSize is < 1 or > 1_000 || options.MaxCachedResources is < 1 or > 100_000 ||
            options.MaxCachedPersonOverwrites is < 1 or > 1_000_000 ||
            options.HeldChangeWait <= TimeSpan.Zero || options.HeldChangeWait > TimeSpan.FromMinutes(1))
            throw new InvalidOperationException("Koan:Identity:Roles contains an invalid or unsafe bound.");
        return options;
    }

    /// <summary>
    /// A key — a role key, a person id, a resource id, a subject, a permission, a metadata key, a cursor's key: trimmed,
    /// present, at most <paramref name="max"/> characters, well-formed UTF-16 and free of control characters, so no two
    /// keys composed into one id or one stored value can be confused.
    /// </summary>
    public static string Require(string? value, string name, int max = 256)
    {
        var result = RequireText(value, name, max);
        if (!IsKey(result))
            throw new ArgumentException($"{name} must not contain a control character or an unpaired surrogate.", name);
        return result;
    }

    /// <summary>Display text — a role's name, a metadata value, a search: trimmed, present, at most <paramref name="max"/> characters.</summary>
    public static string RequireText(string? value, string name, int max = 256)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result) || result.Length > max)
            throw new ArgumentException($"{name} is required and must be at most {max} characters.", name);
        return result;
    }

    /// <summary>The trimmed value when it is a key: present, at most <paramref name="max"/> characters, no control character.</summary>
    public static bool TryRequire(string? value, out string result, int max = 256)
    {
        result = value?.Trim() ?? "";
        return result.Length > 0 && result.Length <= max && IsKey(result);
    }

    /// <summary>Whether <paramref name="value"/> holds no control character and no surrogate without its pair.</summary>
    private static bool IsKey(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c) || char.IsLowSurrogate(c)) return false;
            if (!char.IsHighSurrogate(c)) continue;
            if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return false;
            i++;
        }

        return true;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// The id of the row <paramref name="kind"/> names by <paramref name="parts"/>: the SHA-256 of the parts, each prefixed
    /// with its length, so parts that hold any text compose one id only from the same parts. The text is encoded strictly,
    /// so a part that is not well-formed UTF-16 is refused rather than hashed as the replacement character.
    /// </summary>
    public static string ComposeId(string kind, params string[] parts)
    {
        var text = string.Join('\u001f', [kind, .. parts.Select(part => $"{part.Length}:{part}")]);
        try { return Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(text))); }
        catch (EncoderFallbackException invalid)
        {
            throw new ArgumentException("A key composed into an id is not well-formed UTF-16.", nameof(parts), invalid);
        }
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
