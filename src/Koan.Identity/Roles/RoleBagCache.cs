using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

/// <summary>
/// The compiled state role authorization reads: one bag per person (and one for the anonymous visitor), and one
/// overwrite set per resource. Both are bounded, single-flight and invalidated by generation; a warm read is
/// cache-only.
/// </summary>
public sealed class RoleBagCache
{
    private readonly RoleOptions _options;
    private readonly CompiledStateMap<BagKey, RoleBag> _bags;
    private readonly CompiledStateMap<string, PermissionOverwrite[]> _overwrites;

    public RoleBagCache(IOptions<RoleOptions> options)
    {
        _options = RoleContract.Validate(options.Value);
        _bags = new(_options.MaxCachedBags, CompileBag);
        _overwrites = new(_options.MaxCachedResources, CompileOverwrites);
    }

    public Task<RoleBag> Get(string personId, CancellationToken ct)
        => _bags.Get(new BagKey(true, RoleContract.Require(personId, nameof(personId))), ct);

    internal Task<RoleBag> Anonymous(CancellationToken ct) => _bags.Get(BagKey.Anonymous, ct);

    public void Invalidate(string personId) => _bags.Invalidate(new BagKey(true, personId));
    public void Invalidate(IEnumerable<string> people) { foreach (var person in people.Distinct(StringComparer.Ordinal)) Invalidate(person); }

    /// <summary>Invalidates every bag, the anonymous one included: what a change to an implicit role affects.</summary>
    public void InvalidateAll() => _bags.InvalidateAll();

    /// <summary>Invalidates the compiled overwrite set of one resource: what an overwrite change affects.</summary>
    public void InvalidateOverwrites(string resourceId) => _overwrites.Invalidate(resourceId);

    /// <summary>The compiled overwrite set of a resource, shared by every reader; never handed out as is.</summary>
    internal Task<PermissionOverwrite[]> Overwrites(string resourceId, CancellationToken ct)
        => _overwrites.Get(RoleContract.Require(resourceId, nameof(resourceId)), ct);

    private async Task<RoleBag> CompileBag(BagKey key, CancellationToken ct)
    {
        string[] implicitKeys = key.Authenticated ? [RoleTokens.Everyone, RoleTokens.Authenticated] : [RoleTokens.Everyone];
        var implicitRoles = (await Role.Get(implicitKeys, ct).ConfigureAwait(false)).OfType<Role>().ToList();
        IReadOnlyList<Role> memberRoles = [];
        if (key.Authenticated)
        {
            var personId = key.PersonId;
            var query = new QueryDefinition { Page = 1, PageSize = _options.MaxRolesPerPerson + 1 };
            memberRoles = await Role.Query(role => role.Members.Contains(personId), query, ct).ConfigureAwait(false);
            if (memberRoles.Count > _options.MaxRolesPerPerson) throw new InvalidOperationException($"Person role count exceeds the configured bound of {_options.MaxRolesPerPerson}.");
        }

        var permissions = memberRoles.Concat(implicitRoles)
            .SelectMany(role => role.Permissions.Where(permission => permission.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal)));
        return new RoleBag(key.Authenticated ? key.PersonId : null, key.Authenticated, memberRoles.Select(role => role.Id), permissions);
    }

    private async Task<PermissionOverwrite[]> CompileOverwrites(string resourceId, CancellationToken ct)
    {
        var query = new QueryDefinition { Page = 1, PageSize = _options.MaxOverwritesPerResource + 1 };
        var rows = await PermissionOverwrite.Query(row => row.ResourceId == resourceId, query, ct).ConfigureAwait(false);
        if (rows.Count > _options.MaxOverwritesPerResource)
            throw new InvalidOperationException(
                $"Resource '{resourceId}' holds more overwrites than the configured bound of {_options.MaxOverwritesPerResource} " +
                $"({RoleOptions.SectionPath}:{nameof(RoleOptions.MaxOverwritesPerResource)}); remove overwrites on it or raise the bound.");
        return rows.OrderBy(row => row.SubjectKind).ThenBy(row => row.SubjectId, StringComparer.Ordinal).ToArray();
    }

    private readonly record struct BagKey(bool Authenticated, string PersonId)
    {
        public static BagKey Anonymous { get; } = new(false, "");
    }
}
