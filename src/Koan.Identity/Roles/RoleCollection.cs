using System.Collections.Concurrent;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

/// <summary>
/// The only supported mutation and compilation surface for server roles and their per-resource overwrites. Every
/// change commits immediately: a change asked for inside a deferred <see cref="EntityContext.Transaction(string)"/>
/// scope is refused before any write.
/// </summary>
public sealed class RoleCollection(RoleBagCache bags, IOptions<RoleOptions> options, IIdentityActorAccessor? actor = null)
{
    private readonly RoleOptions _options = RoleContract.Validate(options.Value);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _overwriteLock = new(1, 1);

    public Task<RoleBag> Bag(string? personId, bool authenticated, CancellationToken ct = default)
        => authenticated ? bags.Get(RoleContract.Require(personId, nameof(personId)), ct) : bags.Anonymous(ct);
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

    /// <summary>
    /// Deletes a role and, with it, the overwrites that name it, so a role defined later under the same key starts
    /// with none. Every before-event — the role's and each overwrite's — runs before any write. The overwrites of
    /// <see cref="RoleTokens.Everyone"/> and <see cref="RoleTokens.Authenticated"/> stay: those roles hold every bag
    /// whether or not a row defines their permissions.
    /// </summary>
    public async Task<bool> Delete(string roleKey, CancellationToken ct = default)
    {
        DemandImmediate();
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        await using var gate = await Enter(roleKey, ct);
        await using var overwriteGate = await EnterOverwrites(ct);
        var current = await Role.Get(roleKey, ct); if (current is null) return false;
        IReadOnlyList<PermissionOverwrite> overwrites = RoleContract.IsImplicitRole(roleKey) ? [] : await OverwritesOf(OverwriteSubjectKind.Role, roleKey, ct);
        var before = Context(current, current, null, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(RoleEventKind.RoleDeleting, before);
        var removals = overwrites.Select(row => new OverwriteChangeContext(row, row, actor?.CurrentActorSubject, RoleChangePhase.Before, ct)).ToList();
        foreach (var removal in removals) await RoleEventRegistry.Before(RoleEventKind.OverwriteRemoving, removal);
        foreach (var row in overwrites)
        {
            using (RoleMutationGuard.Allow<PermissionOverwrite>(row.Id, EntityLifecycleOperation.Remove)) await row.Remove(ct);
            bags.InvalidateOverwrites(row.ResourceId);
        }
        using (RoleMutationGuard.Allow<Role>(roleKey, EntityLifecycleOperation.Remove)) await current.Remove(ct);
        InvalidateBags(roleKey, current.Members);
        foreach (var removal in removals) await RoleEventRegistry.After(RoleEventKind.OverwriteRemoved, removal with { Phase = RoleChangePhase.After });
        await RoleEventRegistry.After(RoleEventKind.RoleDeleted, before with { Phase = RoleChangePhase.After });
        return true;
    }

    /// <summary>The overwrites on one resource, for every subject, from the compiled cache. Each call returns its own
    /// copies; pass them to <see cref="RoleBag.On"/>.</summary>
    public async Task<IReadOnlyList<PermissionOverwrite>> Overwrites(string resourceId, CancellationToken ct = default)
        => (await bags.Overwrites(resourceId, ct)).Select(row => row.Duplicate()).ToArray();

    /// <summary>
    /// Sets what <paramref name="subjectId"/> may and may not do on <paramref name="resourceId"/>, replacing its
    /// overwrite there. The subject is a role key — <see cref="RoleTokens.Everyone"/> for everyone — or a person id;
    /// a role other than the implicit ones must exist. <paramref name="allow"/> and <paramref name="deny"/> are
    /// <c>global:*</c> permissions, never <see cref="RoleTokens.Administrator"/>; a permission in both is allowed, as
    /// within one Discord step. A new overwrite is refused when its resource or its subject already holds the
    /// configured maximum. <paramref name="expiresAt"/>, when set, is when it stops applying.
    /// </summary>
    public async Task<PermissionOverwrite> Overwrite(string resourceId, OverwriteSubjectKind subjectKind, string subjectId,
        IEnumerable<string> allow, IEnumerable<string> deny, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        DemandImmediate();
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        subjectId = RequireSubject(subjectKind, subjectId);
        var allowed = NormalizeOverwritePermissions(allow, nameof(allow));
        var denied = NormalizeOverwritePermissions(deny, nameof(deny));
        if (allowed.Length == 0 && denied.Length == 0)
            throw new ArgumentException("An overwrite allows or denies at least one permission; to clear one, remove it.", nameof(allow));
        if (expiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
            throw new ArgumentException("expiresAt must be in the future; to end an overwrite now, remove it.", nameof(expiresAt));

        await using var roleGate = subjectKind == OverwriteSubjectKind.Role ? await Enter(subjectId, ct) : null;
        await using var gate = await EnterOverwrites(ct);
        if (subjectKind == OverwriteSubjectKind.Role && !RoleContract.IsImplicitRole(subjectId) && await Role.Get(subjectId, ct) is null)
            throw new KeyNotFoundException($"Role '{subjectId}' does not exist.");
        var id = PermissionOverwrite.KeyFor(resourceId, subjectKind, subjectId);
        var previous = await PermissionOverwrite.Get(id, ct);
        var next = new PermissionOverwrite
        {
            Id = id, ResourceId = resourceId, SubjectKind = subjectKind, SubjectId = subjectId, Allow = [.. allowed],
            Deny = [.. denied], ExpiresAt = expiresAt, UpdatedBy = actor?.CurrentActorSubject,
        };
        if (previous is not null && Equivalent(previous, next)) return previous;
        if (previous is null) await DemandRoom(resourceId, subjectKind, subjectId, ct);
        return await SaveOverwrite(previous, next, ct);
    }

    /// <summary>Removes the overwrite of <paramref name="subjectId"/> on <paramref name="resourceId"/>; false when there is none.</summary>
    public async Task<bool> RemoveOverwrite(string resourceId, OverwriteSubjectKind subjectKind, string subjectId, CancellationToken ct = default)
    {
        DemandImmediate();
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        subjectId = RequireSubject(subjectKind, subjectId);
        await using var gate = await EnterOverwrites(ct);
        var current = await PermissionOverwrite.Get(PermissionOverwrite.KeyFor(resourceId, subjectKind, subjectId), ct);
        if (current is null) return false;
        await RemoveOverwrite(current, ct);
        return true;
    }

    /// <summary>
    /// Takes one permission out of an overwrite's allow list or deny list and leaves the rest as it was, so revoking an
    /// allow never lifts a deny; the overwrite is removed only when both lists are then empty. False when the
    /// overwrite does not exist, does not name the permission in that list, or either value cannot be one.
    /// </summary>
    internal async Task<bool> RevokeFromOverwrite(string? overwriteId, bool deny, string? permission, CancellationToken ct)
    {
        DemandImmediate();
        if (!RoleContract.TryRequire(overwriteId, out var id) || !RoleContract.TryRequirePermission(permission, out var token)) return false;
        (overwriteId, permission) = (id, token);
        await using var gate = await EnterOverwrites(ct);
        var previous = await PermissionOverwrite.Get(overwriteId, ct);
        if (previous is null || !(deny ? previous.Deny : previous.Allow).Contains(permission, StringComparer.Ordinal)) return false;
        var next = previous.Duplicate();
        next.UpdatedBy = actor?.CurrentActorSubject;
        (deny ? next.Deny : next.Allow).Remove(permission);
        if (next.Allow.Count == 0 && next.Deny.Count == 0) await RemoveOverwrite(previous, ct);
        else await SaveOverwrite(previous, next, ct);
        return true;
    }

    /// <summary>Saves an overwrite change; the overwrite lock is held.</summary>
    private async Task<PermissionOverwrite> SaveOverwrite(PermissionOverwrite? previous, PermissionOverwrite next, CancellationToken ct)
    {
        var context = new OverwriteChangeContext(previous, next, actor?.CurrentActorSubject, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(RoleEventKind.OverwriteChanging, context);
        using (RoleMutationGuard.Allow<PermissionOverwrite>(next.Id, EntityLifecycleOperation.Upsert)) next = await next.Save(ct);
        bags.InvalidateOverwrites(next.ResourceId);
        await RoleEventRegistry.After(RoleEventKind.OverwriteChanged, context with { Current = next, Phase = RoleChangePhase.After });
        return next;
    }

    /// <summary>Removes an overwrite; the overwrite lock is held.</summary>
    private async Task RemoveOverwrite(PermissionOverwrite current, CancellationToken ct)
    {
        var context = new OverwriteChangeContext(current, current, actor?.CurrentActorSubject, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(RoleEventKind.OverwriteRemoving, context);
        using (RoleMutationGuard.Allow<PermissionOverwrite>(current.Id, EntityLifecycleOperation.Remove)) await current.Remove(ct);
        bags.InvalidateOverwrites(current.ResourceId);
        await RoleEventRegistry.After(RoleEventKind.OverwriteRemoved, context with { Phase = RoleChangePhase.After });
    }

    /// <summary>The overwrites one subject holds across resources, bounded by the per-subject maximum.</summary>
    internal async Task<IReadOnlyList<PermissionOverwrite>> OverwritesOf(OverwriteSubjectKind subjectKind, string subjectId, CancellationToken ct)
    {
        var query = new QueryDefinition { Page = 1, PageSize = _options.MaxOverwritesPerSubject + 1 };
        var rows = await PermissionOverwrite.Query(row => row.SubjectKind == subjectKind && row.SubjectId == subjectId, query, ct);
        if (rows.Count > _options.MaxOverwritesPerSubject) throw SubjectBound(subjectKind, subjectId);
        return rows;
    }

    /// <summary>The overwrites, across resources, that apply to a bag: on everyone, on its other roles, on its person.</summary>
    internal async Task<IReadOnlyList<PermissionOverwrite>> OverwritesFor(RoleBag bag, CancellationToken ct)
    {
        var subjects = bag.Roles.ToList();
        if (bag.PersonId is { } person && !subjects.Contains(person, StringComparer.Ordinal)) subjects.Add(person);
        var bound = (int)Math.Min(int.MaxValue - 1, 2L * subjects.Count * _options.MaxOverwritesPerSubject);
        var rows = await PermissionOverwrite.Query(row => subjects.Contains(row.SubjectId), new QueryDefinition { Page = 1, PageSize = bound + 1 }, ct);
        if (rows.Count > bound) throw new InvalidOperationException($"The overwrites of person '{bag.PersonId}' and their roles exceed the configured bound of {_options.MaxOverwritesPerSubject} per subject.");
        return rows.Where(row => row.SubjectKind == OverwriteSubjectKind.Role ? bag.InRole(row.SubjectId) : row.SubjectId == bag.PersonId).ToList();
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
                if (members.Count > _options.MaxMembersPerRole)
                    throw new RoleBoundException(Key(nameof(RoleOptions.MaxMembersPerRole)),
                        $"Role '{roleKey}' already holds the configured maximum of {_options.MaxMembersPerRole} members " +
                        $"({RoleOptions.SectionPath}:{nameof(RoleOptions.MaxMembersPerRole)}). Remove a member, or raise the bound.");
                return Task.FromResult(Copy(current, members: members.Order(StringComparer.Ordinal).ToArray()));
            }, ct);
    }

    private async Task<Role> Mutate(string roleKey, string? subject, RoleEventKind beforeKind, RoleEventKind afterKind,
        Func<Role?, Task<Role>> change, CancellationToken ct)
    {
        DemandImmediate();
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        await using var gate = await Enter(roleKey, ct);
        var previous = await Role.Get(roleKey, ct); var next = await change(previous);
        if (ReferenceEquals(previous, next) || (previous is not null && Equivalent(previous, next))) return previous ?? next;
        var context = Context(previous ?? next, next, subject, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(beforeKind, context);
        var alsoPermissions = previous is not null && beforeKind == RoleEventKind.RoleChanging &&
                              !previous.Permissions.SequenceEqual(next.Permissions, StringComparer.Ordinal);
        if (alsoPermissions) await RoleEventRegistry.Before(RoleEventKind.PermissionsChanging, context);
        using (RoleMutationGuard.Allow<Role>(roleKey, EntityLifecycleOperation.Upsert)) next = await next.Save(ct);
        InvalidateBags(roleKey, (previous?.Members ?? []).Concat(next.Members));
        if (alsoPermissions) await RoleEventRegistry.After(RoleEventKind.PermissionsChanged, context with { Current = next, Phase = RoleChangePhase.After });
        await RoleEventRegistry.After(afterKind, context with { Current = next, Phase = RoleChangePhase.After });
        return next;
    }

    /// <summary>
    /// Refuses, before any lock is taken, a change asked for by a role lifecycle handler, and a change asked for inside
    /// a deferred scope. A role or overwrite change is a read-modify-write under this collection's lock, with
    /// before-events, cache invalidation and after-events around one write; a deferred scope would hold the write
    /// until its commit while the rest ran now.
    /// </summary>
    private static void DemandImmediate()
    {
        RoleEventRegistry.DemandMutationAllowed();
        if (EntityContext.Current?.TransactionCoordinator is not null)
            throw new NotSupportedException(
                "A role or overwrite change commits immediately and cannot join a deferred EntityContext.Transaction scope: " +
                "its events and cache invalidation would run before a write that the scope holds until its commit. " +
                "Make the change after EntityContext.Commit(), outside the scope.");
    }

    private void InvalidateBags(string roleKey, IEnumerable<string> members)
    {
        if (RoleContract.IsImplicitRole(roleKey)) bags.InvalidateAll();
        else bags.Invalidate(members);
    }

    private async Task DemandRoom(string resourceId, OverwriteSubjectKind subjectKind, string subjectId, CancellationToken ct)
    {
        var onResource = await PermissionOverwrite.Count.Where(row => row.ResourceId == resourceId, CountStrategy.Exact, ct);
        if (onResource >= _options.MaxOverwritesPerResource)
            throw new RoleBoundException(Key(nameof(RoleOptions.MaxOverwritesPerResource)),
                $"Resource '{resourceId}' already holds the configured maximum of {_options.MaxOverwritesPerResource} overwrites " +
                $"({RoleOptions.SectionPath}:{nameof(RoleOptions.MaxOverwritesPerResource)}). Remove an overwrite on it, or raise the bound.");
        var ofSubject = await PermissionOverwrite.Count.Where(row => row.SubjectKind == subjectKind && row.SubjectId == subjectId, CountStrategy.Exact, ct);
        if (ofSubject >= _options.MaxOverwritesPerSubject) throw SubjectBound(subjectKind, subjectId);
    }

    private static string Key(string bound) => $"{RoleOptions.SectionPath}:{bound}";

    private RoleBoundException SubjectBound(OverwriteSubjectKind subjectKind, string subjectId) => new(Key(nameof(RoleOptions.MaxOverwritesPerSubject)),
        $"{subjectKind} '{subjectId}' already holds the configured maximum of {_options.MaxOverwritesPerSubject} overwrites " +
        $"({RoleOptions.SectionPath}:{nameof(RoleOptions.MaxOverwritesPerSubject)}). Remove one of its overwrites, or raise the bound.");

    private static string RequireSubject(OverwriteSubjectKind subjectKind, string subjectId)
    {
        if (!Enum.IsDefined(subjectKind)) throw new ArgumentException($"Subject kind '{subjectKind}' is neither Role nor Person.", nameof(subjectKind));
        return RoleContract.Require(subjectId, nameof(subjectId));
    }

    private string[] NormalizeOverwritePermissions(IEnumerable<string>? permissions, string name)
    {
        var result = RoleContract.Normalize(permissions, _options.MaxPermissionsPerRole, name, allowEmpty: true);
        foreach (var permission in result)
        {
            RoleContract.RequirePermission(permission, name);
            if (permission == RoleTokens.Administrator)
                throw new ArgumentException($"{RoleTokens.Administrator} bypasses overwrites and is granted by a role only; an overwrite cannot allow or deny it.", name);
        }

        return result;
    }

    private async Task<IAsyncDisposable> Enter(string key, CancellationToken ct) { var s = _locks.GetOrAdd(key, _ => new(1, 1)); await s.WaitAsync(ct); return new Releaser(s); }
    private async Task<IAsyncDisposable> EnterOverwrites(CancellationToken ct) { await _overwriteLock.WaitAsync(ct); return new Releaser(_overwriteLock); }
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
    private static bool Equivalent(PermissionOverwrite left, PermissionOverwrite right) => left.ResourceId == right.ResourceId &&
        left.SubjectKind == right.SubjectKind && left.SubjectId == right.SubjectId && left.ExpiresAt == right.ExpiresAt &&
        left.Allow.SequenceEqual(right.Allow, StringComparer.Ordinal) && left.Deny.SequenceEqual(right.Deny, StringComparer.Ordinal);
    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable { public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; } }
}
