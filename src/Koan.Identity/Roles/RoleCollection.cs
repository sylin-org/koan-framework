using System.Collections.Concurrent;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

/// <summary>The only supported mutation and bag-compilation surface for server roles.</summary>
public sealed class RoleCollection(RoleBagCache bags, IOptions<RoleOptions> options, IIdentityActorAccessor? actor = null)
{
    private readonly RoleOptions _options = RoleContract.Validate(options.Value);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public Task<RoleBag> Bag(string? personId, bool authenticated, CancellationToken ct = default)
        => authenticated ? bags.Get(RoleContract.Require(personId, nameof(personId)), ct) : Task.FromResult(RoleBag.Anonymous);
    public Task<Role?> Get(string roleKey, CancellationToken ct = default) => Role.Get(RoleContract.Require(roleKey, nameof(roleKey)), ct);
    public async Task<IReadOnlyList<Role>> ForPerson(string personId, CancellationToken ct = default)
    {
        personId = RoleContract.Require(personId, nameof(personId));
        var result = await Role.Query(role => role.Members.Contains(personId), new QueryDefinition { Page = 1, PageSize = _options.MaxRolesPerPerson + 1 }, ct);
        if (result.Count > _options.MaxRolesPerPerson) throw new InvalidOperationException($"Person role count exceeds the configured bound of {_options.MaxRolesPerPerson}.");
        return result;
    }

    public Task<Role> Define(string roleKey, string name, IEnumerable<string>? permissions = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Mutate(roleKey, null, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current =>
        {
            var nextPermissions = permissions is null ? current?.Permissions.ToArray() ?? [] : NormalizePermissions(permissions);
            return Task.FromResult(new Role { Id = roleKey, Name = RoleContract.Require(name, nameof(name), 160), Permissions = [.. nextPermissions], Members = current?.Members.ToList() ?? [], Metadata = metadata is null ? current?.Metadata.ToDictionary() ?? [] : NormalizeMetadata(metadata), UpdatedBy = actor?.CurrentActorSubject });
        }, ct);

    public Task<Role> Update(string roleKey, string name, IEnumerable<string> permissions,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Mutate(roleKey, null, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current =>
            Task.FromResult(Copy(Require(current, roleKey), name: RoleContract.Require(name, nameof(name), 160),
                permissions: NormalizePermissions(permissions), metadata: NormalizeMetadata(metadata))), ct);

    public Task<Role> Rename(string roleKey, string name, CancellationToken ct = default)
        => Mutate(roleKey, null, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current => Task.FromResult(Copy(Require(current, roleKey), name: RoleContract.Require(name, nameof(name), 160))), ct);

    public Task<Role> SetPermissions(string roleKey, IEnumerable<string> permissions, CancellationToken ct = default)
        => Mutate(roleKey, null, RoleEventKind.PermissionsChanging, RoleEventKind.PermissionsChanged, current => Task.FromResult(Copy(Require(current, roleKey), permissions: NormalizePermissions(permissions))), ct);

    public Task<Role> Add(string roleKey, string personId, CancellationToken ct = default)
        => ChangeMember(roleKey, personId, true, ct);
    public Task<Role> Remove(string roleKey, string personId, CancellationToken ct = default)
        => ChangeMember(roleKey, personId, false, ct);

    public async Task<RolePage> Page(string? search = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > _options.MaxPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        search = string.IsNullOrWhiteSpace(search) ? null : RoleContract.Require(search, nameof(search), 160);
        var query = new QueryDefinition { Page = page, PageSize = pageSize };
        var result = search is null ? await Role.AllWithCount(query, ct) : await Role.QueryWithCount(role => role.Id.Contains(search) || role.Name.Contains(search), query, ct);
        return new RolePage(result.Items, result.TotalCount, page, pageSize);
    }

    public async Task<bool> Delete(string roleKey, CancellationToken ct = default)
    {
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        await using var gate = await Enter(roleKey, ct);
        RoleEventRegistry.DemandMutationAllowed();
        var current = await Role.Get(roleKey, ct); if (current is null) return false;
        var before = Context(current, current, null, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(RoleEventKind.RoleDeleting, before);
        using (RoleMutationGuard.Allow(roleKey, EntityLifecycleOperation.Remove)) await current.Remove(ct);
        bags.Invalidate(current.Members);
        await RoleEventRegistry.After(RoleEventKind.RoleDeleted, before with { Phase = RoleChangePhase.After });
        return true;
    }

    private Task<Role> ChangeMember(string roleKey, string personId, bool add, CancellationToken ct)
    {
        personId = RoleContract.Require(personId, nameof(personId));
        return Mutate(roleKey, personId, add ? RoleEventKind.MemberAdding : RoleEventKind.MemberRemoving,
            add ? RoleEventKind.MemberAdded : RoleEventKind.MemberRemoved, current =>
            {
                current = Require(current, roleKey); var members = current.Members.ToHashSet(StringComparer.Ordinal);
                var changed = add ? members.Add(personId) : members.Remove(personId);
                if (!changed) return Task.FromResult(current);
                if (members.Count > _options.MaxMembersPerRole) throw new InvalidOperationException($"Role member count exceeds the configured bound of {_options.MaxMembersPerRole}.");
                return Task.FromResult(Copy(current, members: members.Order(StringComparer.Ordinal).ToArray()));
            }, ct);
    }

    private async Task<Role> Mutate(string roleKey, string? subject, RoleEventKind beforeKind, RoleEventKind afterKind,
        Func<Role?, Task<Role>> change, CancellationToken ct)
    {
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        await using var gate = await Enter(roleKey, ct); RoleEventRegistry.DemandMutationAllowed();
        var previous = await Role.Get(roleKey, ct); var next = await change(previous);
        if (ReferenceEquals(previous, next) || (previous is not null && Equivalent(previous, next))) return previous ?? next;
        var context = Context(previous ?? next, next, subject, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(beforeKind, context);
        var alsoPermissions = previous is not null && beforeKind == RoleEventKind.RoleChanging &&
                              !previous.Permissions.SequenceEqual(next.Permissions, StringComparer.Ordinal);
        if (alsoPermissions) await RoleEventRegistry.Before(RoleEventKind.PermissionsChanging, context);
        using (RoleMutationGuard.Allow(roleKey, EntityLifecycleOperation.Upsert)) next = await next.Save(ct);
        bags.Invalidate((previous?.Members ?? []).Concat(next.Members));
        if (alsoPermissions) await RoleEventRegistry.After(RoleEventKind.PermissionsChanged, context with { Current = next, Phase = RoleChangePhase.After });
        await RoleEventRegistry.After(afterKind, context with { Current = next, Phase = RoleChangePhase.After });
        return next;
    }

    private async Task<IAsyncDisposable> Enter(string key, CancellationToken ct) { var s = _locks.GetOrAdd(key, _ => new(1, 1)); await s.WaitAsync(ct); return new Releaser(s); }
    private RoleChangeContext Context(Role previous, Role current, string? subject, RoleChangePhase phase, CancellationToken ct) => new(previous, current, subject, actor?.CurrentActorSubject, phase, ct);
    private string[] NormalizePermissions(IEnumerable<string> p) => RoleContract.Normalize(p, _options.MaxPermissionsPerRole, nameof(p), allowEmpty: true);
    private static Role Require(Role? role, string key) => role ?? throw new KeyNotFoundException($"Role '{key}' does not exist.");
    private Dictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return [];
        if (metadata.Count > _options.MaxMetadataEntries) throw new ArgumentException($"Metadata exceeds the configured bound of {_options.MaxMetadataEntries} entries.", nameof(metadata));
        return metadata.ToDictionary(entry => RoleContract.Require(entry.Key, "metadata key", 64), entry => RoleContract.Require(entry.Value, "metadata value", 512), StringComparer.Ordinal);
    }
    private Role Copy(Role r, string? name = null, IEnumerable<string>? permissions = null, IEnumerable<string>? members = null, IReadOnlyDictionary<string, string>? metadata = null) => new() { Id = r.Id, Name = name ?? r.Name, Permissions = [.. permissions ?? r.Permissions], Members = [.. members ?? r.Members], Metadata = metadata?.ToDictionary() ?? r.Metadata.ToDictionary(), UpdatedBy = actor?.CurrentActorSubject };
    private static bool Equivalent(Role left, Role right) => left.Id == right.Id && left.Name == right.Name &&
        left.Permissions.SequenceEqual(right.Permissions, StringComparer.Ordinal) &&
        left.Members.SequenceEqual(right.Members, StringComparer.Ordinal) &&
        left.Metadata.Count == right.Metadata.Count && left.Metadata.All(entry => right.Metadata.TryGetValue(entry.Key, out var value) && value == entry.Value);
    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable { public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; } }
}
