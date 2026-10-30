using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

/// <summary>
/// The compiled state role authorization reads: one bag per person (and one for the anonymous visitor), each
/// resource's role overwrites, and each person's own overwrite on a resource or its absence. Each is single-flight,
/// invalidated by generation, and sized by a capacity past which it evicts an entry nothing has read lately; a warm
/// read is cache-only.
/// </summary>
public sealed class RoleBagCache
{
    private readonly RoleOptions _options;
    private readonly CompiledStateMap<BagKey, RoleBag> _bags;
    private readonly CompiledStateMap<string, IReadOnlyDictionary<string, PermissionOverwrite>> _roleOverwrites;
    private readonly CompiledStateMap<PersonOverwriteKey, PermissionOverwrite?> _personOverwrites;

    public RoleBagCache(IOptions<RoleOptions> options)
    {
        _options = RoleContract.Validate(options.Value);
        _bags = new(_options.MaxCachedBags, CompileBag);
        _roleOverwrites = new(_options.MaxCachedResources, CompileRoleOverwrites);
        _personOverwrites = new(_options.MaxCachedPersonOverwrites, CompilePersonOverwrite);
    }

    public Task<RoleBag> Get(string personId, CancellationToken ct)
        => _bags.Get(new BagKey(true, RoleContract.Require(personId, nameof(personId))), ct);

    internal Task<RoleBag> Anonymous(CancellationToken ct) => _bags.Get(BagKey.Anonymous, ct);

    public void Invalidate(string personId) => _bags.Invalidate(new BagKey(true, personId));
    public void Invalidate(IEnumerable<string> people) { foreach (var person in people.Distinct(StringComparer.Ordinal)) Invalidate(person); }

    /// <summary>Invalidates every bag, the anonymous one included: what a change to a role's permissions affects.</summary>
    public void InvalidateAll() => _bags.InvalidateAll();

    /// <summary>
    /// Invalidates the compiled overwrites of one resource — its role overwrites and each person's own: what a change
    /// to any overwrite on it affects.
    /// </summary>
    public void InvalidateOverwrites(string resourceId)
    {
        _roleOverwrites.Invalidate(resourceId);
        _personOverwrites.InvalidateWhere(key => StringComparer.Ordinal.Equals(key.ResourceId, resourceId));
    }

    /// <summary>Invalidates the one compiled entry a change to <paramref name="overwrite"/> affects.</summary>
    internal void InvalidateOverwrite(PermissionOverwrite overwrite)
    {
        if (overwrite.SubjectKind == OverwriteSubjectKind.Person)
            _personOverwrites.Invalidate(new PersonOverwriteKey(overwrite.ResourceId, overwrite.SubjectId));
        else
            _roleOverwrites.Invalidate(overwrite.ResourceId);
    }

    /// <summary>A resource's role overwrites by role key, shared by every reader; never handed out as is.</summary>
    internal Task<IReadOnlyDictionary<string, PermissionOverwrite>> RoleOverwrites(string resourceId, CancellationToken ct)
        => _roleOverwrites.Get(resourceId, ct);

    /// <summary>A person's own overwrite on a resource, or null; shared by every reader, never handed out as is.</summary>
    internal Task<PermissionOverwrite?> PersonOverwrite(string resourceId, string personId, CancellationToken ct)
        => _personOverwrites.Get(new PersonOverwriteKey(resourceId, personId), ct);

    /// <summary>Whether a person's bag is compiled now, without reading it.</summary>
    internal bool HoldsBag(string personId) => _bags.Contains(new BagKey(true, personId));

    /// <summary>How many bags are compiled now.</summary>
    internal int BagCount => _bags.Count;

    private async Task<RoleBag> CompileBag(BagKey key, CancellationToken ct)
    {
        string[] implicitKeys = key.Authenticated ? [RoleTokens.Everyone, RoleTokens.Authenticated] : [RoleTokens.Everyone];
        var implicitRoles = await RoleReads.ExactRoles(implicitKeys, ct).ConfigureAwait(false);
        var memberRoles = key.Authenticated
            ? await RoleReads.RolesOf(key.PersonId, _options.MaxRolesPerPerson, _options.MaxPageSize, ct).ConfigureAwait(false)
            : [];

        var permissions = memberRoles.Concat(implicitRoles)
            .SelectMany(role => role.Permissions.Where(permission => permission.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal)));
        return new RoleBag(key.Authenticated ? key.PersonId : null, key.Authenticated, memberRoles.Select(role => role.Key), permissions);
    }

    /// <summary>
    /// Every role overwrite on a resource, read a page at a time and kept only when its resource is exactly this one;
    /// there is at most one per role.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, PermissionOverwrite>> CompileRoleOverwrites(string resourceId, CancellationToken ct)
    {
        var rows = new Dictionary<string, PermissionOverwrite>(StringComparer.Ordinal);
        for (string? after = null; ;)
        {
            var (page, next) = await RoleReads.Page<PermissionOverwrite>(
                row => row.ResourceId == resourceId && row.SubjectKind == OverwriteSubjectKind.Role,
                row => row.SubjectKind == OverwriteSubjectKind.Role && StringComparer.Ordinal.Equals(row.ResourceId, resourceId),
                row => row.SubjectId, row => row.SubjectId, after, _options.MaxPageSize, ct).ConfigureAwait(false);
            foreach (var row in page) rows[row.SubjectId] = row;
            if ((after = next) is null) return rows;
        }
    }

    /// <summary>A person's own overwrite on a resource: one read by key, kept only when it names exactly them there.</summary>
    private static async Task<PermissionOverwrite?> CompilePersonOverwrite(PersonOverwriteKey key, CancellationToken ct)
        => await PermissionOverwrite.Get(PermissionOverwrite.KeyFor(key.ResourceId, OverwriteSubjectKind.Person, key.PersonId), ct).ConfigureAwait(false) is { } row &&
           row.SubjectKind == OverwriteSubjectKind.Person && StringComparer.Ordinal.Equals(row.ResourceId, key.ResourceId) &&
           StringComparer.Ordinal.Equals(row.SubjectId, key.PersonId)
            ? row
            : null;

    private readonly record struct BagKey(bool Authenticated, string PersonId)
    {
        public static BagKey Anonymous { get; } = new(false, "");
    }

    private readonly record struct PersonOverwriteKey(string ResourceId, string PersonId);
}
