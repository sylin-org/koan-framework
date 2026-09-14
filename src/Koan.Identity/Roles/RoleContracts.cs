namespace Koan.Identity.Roles;

public static class RoleTokens
{
    public const string Everyone = "everyone";
    public const string Authenticated = "authenticated";
    public const string GlobalPrefix = "global:";
    public const string RolePrefix = "role:";
    public const string GroupPrefix = "group:";
}

public sealed class PermissionCriteria
{
    private PermissionCriteria(IEnumerable<string> tokens) => AnyOf = RoleContract.Normalize(tokens, 64, nameof(tokens));
    public IReadOnlyList<string> AnyOf { get; }
    public static PermissionCriteria Any(params string[] tokens) => new(tokens);
}

public sealed class RoleBag
{
    private readonly HashSet<string> _tokens;
    internal RoleBag(string? personId, bool authenticated, IEnumerable<string> tokens)
    {
        PersonId = authenticated ? RoleContract.Require(personId, nameof(personId)) : null;
        IsAuthenticated = authenticated;
        _tokens = new(tokens, StringComparer.Ordinal);
        _tokens.Add(RoleTokens.Everyone);
        if (authenticated) _tokens.Add(RoleTokens.Authenticated);
        Tokens = _tokens.Order(StringComparer.Ordinal).ToArray();
    }
    public string? PersonId { get; }
    public bool IsAuthenticated { get; }
    public IReadOnlyList<string> Tokens { get; }
    public bool Contains(string token) => _tokens.Contains(RoleContract.Require(token, nameof(token)));
    public bool ContainsAny(IEnumerable<string> tokens) => tokens.Any(_tokens.Contains);
    public static RoleBag Anonymous { get; } = new(null, false, []);
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
    public int MaxPermissionsPerRole { get; set; } = 256;
    public int MaxMetadataEntries { get; set; } = 32;
    public int MaxPageSize { get; set; } = 100;
}

public sealed record RolePage(IReadOnlyList<Role> Items, long TotalCount, int Page, int PageSize);

internal static class RoleContract
{
    public static RoleOptions Validate(RoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxCachedBags is < 1 or > 100_000 || options.MaxRolesPerPerson is < 1 or > 10_000 ||
            options.MaxMembersPerRole is < 1 or > 1_000_000 || options.MaxPermissionsPerRole is < 1 or > 10_000 ||
            options.MaxMetadataEntries is < 0 or > 1_000 || options.MaxPageSize is < 1 or > 1_000)
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

    public static string[] Normalize(IEnumerable<string>? values, int max, string name, bool allowEmpty = false)
    {
        var result = (values ?? []).Select(x => Require(x, name)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if ((!allowEmpty && result.Length == 0) || result.Length > max)
            throw new ArgumentException($"{name} must contain {(allowEmpty ? "at most" : "between 1 and")} {max} distinct values.", name);
        return result;
    }
}
