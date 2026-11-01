using System.Collections.Concurrent;
using Koan.Data.Core.Lifecycle;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

/// <summary>
/// The only supported mutation and compilation surface for server roles, their members and their per-resource
/// overwrites. Outside any scope every change commits immediately. Inside <see cref="EntityContext.Hold"/> it joins the
/// hold: it commits or rolls back with everything else there, its compiled state and its after-events wait for the commit,
/// and every other flow's change waits for the hold to end (SEC-0036). A change asked for inside a deferred
/// <see cref="EntityContext.Transaction(string)"/> scope is refused before any write. What grows — a role's members, a
/// resource's or a subject's overwrites — is read a page at a time; evaluating a bag on a resource reads only the rows
/// that bag can meet there. Roles stand in one order (<see cref="Role.Position"/>), kept dense as roles are defined,
/// moved and deleted, and a bag manages the roles below its highest one (<see cref="Manages"/>).
/// </summary>
public sealed class RoleCollection(RoleBagCache bags, IOptions<RoleOptions> options, IIdentityActorAccessor? actor = null)
{
    private readonly RoleOptions _options = RoleContract.Validate(options.Value);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _overwriteLock = new(1, 1);

    // Passed before any other lock. It keeps the locks here from being contended between a hold and any other flow, so a
    // hold never waits for a lock whose holder is waiting for the store the hold keeps.
    private readonly RoleChangeGate _gate = new(options.Value.HeldChangeWait);

    // Every write of a role's row — its own change, and each position a placement, a move or a deletion shifts — is
    // serialized, so the order stays dense within this process and a change never writes back a position that a shift
    // has since moved. Taken after any other lock, and never held while waiting for another.
    private readonly SemaphoreSlim _orderLock = new(1, 1);

    // Joins of one person are serialized, so the roles-per-person bound holds within this process. A lock per person
    // would grow with every person ever added; a fixed set of stripes bounds the memory, and people who share a stripe
    // only wait on each other. Taken after the role's lock, and never while waiting for another lock.
    private const int PersonLockStripes = 64;
    private readonly SemaphoreSlim[] _personLocks = [.. Enumerable.Range(0, PersonLockStripes).Select(_ => new SemaphoreSlim(1, 1))];

    public Task<RoleBag> Bag(string? personId, bool authenticated, CancellationToken ct = default)
        => authenticated ? bags.Get(RoleContract.Require(personId, nameof(personId)), ct) : bags.Anonymous(ct);
    public Task<Role?> Get(string roleKey, CancellationToken ct = default) => RoleReads.ExactRole(RoleContract.Require(roleKey, nameof(roleKey)), ct);

    /// <summary>The roles <paramref name="personId"/> holds, in key order: their memberships read by person.</summary>
    public Task<IReadOnlyList<Role>> ForPerson(string personId, CancellationToken ct = default)
        => RoleReads.RolesOf(RoleContract.Require(personId, nameof(personId)), _options.MaxRolesPerPerson, _options.MaxPageSize, ct);

    /// <summary>
    /// A page of <paramref name="roleKey"/>'s members in person order, at most <paramref name="take"/> (no more than
    /// <see cref="RoleOptions.MaxPageSize"/>). Pass a page's <see cref="RoleMemberPage.Next"/> as <paramref name="after"/>
    /// to read the next; a page can hold fewer rows than <paramref name="take"/> while a next page follows. Whether one
    /// person is a member is their bag's <see cref="RoleBag.InRole"/>, or one read of <see cref="RoleMember.KeyFor"/>.
    /// </summary>
    public async Task<RoleMemberPage> Members(string roleKey, string? after = null, int take = 50, CancellationToken ct = default)
    {
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        RequireTake(take);
        var (rows, next) = await RoleReads.Page<RoleMember>(member => member.RoleKey == roleKey,
            member => StringComparer.Ordinal.Equals(member.RoleKey, roleKey), member => member.PersonId, member => member.PersonId,
            after, take, ct);
        return new RoleMemberPage(rows, next);
    }

    /// <summary>
    /// Defines the role keyed <paramref name="roleKey"/>, or changes it when it exists; a null
    /// <paramref name="permissions"/> or <paramref name="metadata"/> keeps what the role has. A new role is placed at
    /// <paramref name="position"/>, the role there and every role above it moving up one, or at the top when it is null;
    /// pass the creator's <see cref="RoleBag.Highest"/> to place it directly below their highest role. A role that
    /// exists moves to <paramref name="position"/> as <see cref="Move"/> moves it, or stays where it is when it is null.
    /// A position past the top places the role at the top, and one below 1 at the bottom.
    /// <see cref="RoleTokens.Everyone"/> is always at 0, and a new <see cref="RoleTokens.Authenticated"/> is placed at the
    /// bottom of the rest unless a position is given, since every signed-in person holds it.
    /// </summary>
    public Task<Role> Define(string roleKey, string name, IEnumerable<string>? permissions = null,
        IReadOnlyDictionary<string, string>? metadata = null, int? position = null, CancellationToken ct = default)
        => Mutate(roleKey, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current =>
        {
            var nextPermissions = permissions is null ? current?.Permissions.ToArray() ?? [] : NormalizePermissions(permissions);
            return new Role { Id = Role.KeyFor(roleKey), Key = roleKey, Name = RoleContract.RequireText(name, nameof(name), 160), Permissions = [.. nextPermissions], Metadata = metadata is null ? current?.Metadata.ToDictionary() ?? [] : NormalizeMetadata(metadata), Position = current?.Position ?? 0, UpdatedBy = actor?.CurrentActorSubject };
        }, position, ct);

    public Task<Role> Update(string roleKey, string name, IEnumerable<string> permissions,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        => Mutate(roleKey, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current =>
            Copy(Require(current, roleKey), name: RoleContract.RequireText(name, nameof(name), 160),
                permissions: NormalizePermissions(permissions), metadata: NormalizeMetadata(metadata)), null, ct);

    public Task<Role> Rename(string roleKey, string name, CancellationToken ct = default)
        => Mutate(roleKey, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current => Copy(Require(current, roleKey), name: RoleContract.RequireText(name, nameof(name), 160)), null, ct);

    public Task<Role> SetPermissions(string roleKey, IEnumerable<string> permissions, CancellationToken ct = default)
        => Mutate(roleKey, RoleEventKind.PermissionsChanging, RoleEventKind.PermissionsChanged, current => Copy(Require(current, roleKey), permissions: NormalizePermissions(permissions)), null, ct);

    /// <summary>
    /// Moves the role keyed <paramref name="roleKey"/>, which must exist, to <paramref name="position"/>; each role
    /// between its old position and the new one moves one toward the place it left, so the order stays dense. A
    /// position past the top moves it to the top, and one below 1 to the bottom. <see cref="RoleTokens.Everyone"/> stays
    /// at 0: moving it elsewhere is refused. Whether an actor may make the move is <see cref="Manages"/> with
    /// <paramref name="position"/>. A move publishes <c>RoleChanging</c> and <c>RoleChanged</c>, and every bag compiles
    /// again, since the position of the highest role a bag is in can change.
    /// </summary>
    public Task<Role> Move(string roleKey, int position, CancellationToken ct = default)
        => Mutate(roleKey, RoleEventKind.RoleChanging, RoleEventKind.RoleChanged, current => Copy(Require(current, roleKey)), position, ct);

    /// <summary>
    /// Whether <paramref name="actor"/> manages the role keyed <paramref name="roleKey"/> and its members and, given
    /// <paramref name="to"/>, may move it there, by <see cref="RoleBag.Manages"/>: one read of the role. A key no role
    /// has is judged where <see cref="Define"/> would place it: at <paramref name="to"/>, below the actor's highest role
    /// when <paramref name="to"/> is at most their <see cref="RoleBag.Highest"/>, or, without one, at the top, which
    /// only an administrator manages.
    /// </summary>
    public async Task<bool> Manages(RoleBag actor, string roleKey, int? to = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        if (await RoleReads.ExactRole(roleKey, ct) is { } role) return actor.Manages(role, to);
        if (roleKey == RoleTokens.Everyone) return actor.Manages(new Role { Key = roleKey }, to);
        if (actor.Administers) return true;
        var at = to ?? (roleKey == RoleTokens.Authenticated ? 1 : int.MaxValue);
        return Math.Max(at, 1) <= actor.Highest;
    }

    /// <summary>
    /// Adds <paramref name="personId"/> to <paramref name="roleKey"/>, which must exist: one membership row, and the
    /// person's bag compiles again. Adding a member again returns their membership and publishes nothing. A person
    /// already holding <see cref="RoleOptions.MaxRolesPerPerson"/> roles is refused with <see cref="RoleBoundException"/>;
    /// joins of one person are serialized in this process, so the bound holds here. Processes that share a store do
    /// not coordinate, so two of them can each admit the last role, and the bag of a person left over the bound then
    /// fails closed.
    /// </summary>
    public async Task<RoleMember> Add(string roleKey, string personId, CancellationToken ct = default)
    {
        DemandChangeable();
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        personId = RoleContract.Require(personId, nameof(personId));
        await using var turn = await _gate.Enter(ct);
        await using var gate = await Enter(roleKey, ct);
        await using var personGate = await EnterPerson(personId, ct);
        var role = Require(await RoleReads.ExactRole(roleKey, ct), roleKey);
        var id = RoleMember.KeyFor(roleKey, personId);
        if (Exact(await RoleMember.Get(id, ct), roleKey, personId) is { } existing) return existing;
        if ((await RoleReads.MembershipsOf(personId, _options.MaxRolesPerPerson, _options.MaxPageSize, ct)).Count >= _options.MaxRolesPerPerson)
            throw new RoleBoundException(Key(nameof(RoleOptions.MaxRolesPerPerson)),
                $"Person '{personId}' already holds the configured maximum of {_options.MaxRolesPerPerson} roles " +
                $"({RoleOptions.SectionPath}:{nameof(RoleOptions.MaxRolesPerPerson)}). Remove one of their roles, or raise the bound.");

        var context = Context(role, role, personId, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(RoleEventKind.MemberAdding, context);
        var member = new RoleMember { Id = id, RoleKey = roleKey, PersonId = personId, AddedAt = DateTimeOffset.UtcNow, AddedBy = actor?.CurrentActorSubject };
        using (RoleMutationGuard.Allow<RoleMember>(id, EntityLifecycleOperation.Upsert)) member = await member.Save(ct);
        _gate.Invalidate(() => bags.Invalidate(personId));
        await RoleChangeGate.Published(RoleEventKind.MemberAdded, token => context with { Phase = RoleChangePhase.After, CancellationToken = token }, ct);
        return member;
    }

    /// <summary>
    /// Removes <paramref name="personId"/> from <paramref name="roleKey"/>: one membership row, and the person's bag
    /// compiles again. False when they are not a member. A membership whose role no longer exists is removed without
    /// events, since there is no role to name.
    /// </summary>
    public async Task<bool> Remove(string roleKey, string personId, CancellationToken ct = default)
    {
        DemandChangeable();
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        personId = RoleContract.Require(personId, nameof(personId));
        await using var turn = await _gate.Enter(ct);
        await using var gate = await Enter(roleKey, ct);
        var member = Exact(await RoleMember.Get(RoleMember.KeyFor(roleKey, personId), ct), roleKey, personId);
        if (member is null) return false;
        var context = await RoleReads.ExactRole(roleKey, ct) is { } role ? Context(role, role, personId, RoleChangePhase.Before, ct) : null;
        if (context is not null) await RoleEventRegistry.Before(RoleEventKind.MemberRemoving, context);
        using (RoleMutationGuard.Allow<RoleMember>(member.Id, EntityLifecycleOperation.Remove)) await member.Remove(ct);
        _gate.Invalidate(() => bags.Invalidate(personId));
        if (context is not null) await RoleChangeGate.Published(RoleEventKind.MemberRemoved, token => context with { Phase = RoleChangePhase.After, CancellationToken = token }, ct);
        return true;
    }

    /// <summary>
    /// A page of the roles in their order, the highest first, so <see cref="RoleTokens.Everyone"/> ends the last page;
    /// <paramref name="search"/> keeps the roles whose key or name holds it, in the same order. A page holds at most
    /// <see cref="RoleOptions.MaxPageSize"/> roles.
    /// </summary>
    public async Task<RolePage> Page(string? search = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > _options.MaxPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize));
        search = string.IsNullOrWhiteSpace(search) ? null : RoleContract.RequireText(search, nameof(search), 160);
        var query = new QueryDefinition().WithSort<Role>(sort => sort.OrderByDescending(role => role.Position)).WithPagination(page, pageSize);
        var result = search is null ? await Role.AllWithCount(query, ct) : await Role.QueryWithCount(role => role.Key.Contains(search) || role.Name.Contains(search), query, ct);
        return new RolePage(result.Items, result.TotalCount, page, pageSize);
    }

    /// <summary>
    /// Deletes a role and, with it, its members and the overwrites that name it, so a role defined later under the same
    /// key starts with neither. The role's before-event and every overwrite's before-event run before any write. Then
    /// the role is neutralized: its permissions are cleared, the allows of its overwrites are taken out — an overwrite
    /// that allowed only is removed, one that also denies keeps its denies — and every bag compiles again. Then each
    /// membership is removed and its person's bag compiles again, then the overwrites that remain, which only deny, and
    /// the role last. Each is read a page at a time, and each overwrite's after-event is published as it is removed.
    /// No write takes away a deny while the role still grants anything, so no state between them grants more than the
    /// role did; once it is neutralized, a deletion that stops leaves members holding only the role's denies, and a
    /// retry finishes it. The overwrites of <see cref="RoleTokens.Everyone"/> and <see cref="RoleTokens.Authenticated"/>
    /// stay: those roles hold every bag whether or not a row defines their permissions. Once the role is gone, every role
    /// above it moves down one, so the order stays dense, and every bag compiles again. Inside a hold all of it is one
    /// outcome: a deletion that stops rolls back with the hold, and the hold's limit bounds how large a role it can delete.
    /// </summary>
    public async Task<bool> Delete(string roleKey, CancellationToken ct = default)
    {
        DemandChangeable();
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        await using var turn = await _gate.Enter(ct);
        await using var gate = await Enter(roleKey, ct);
        await using var overwriteGate = await EnterOverwrites(ct);
        var current = await RoleReads.ExactRole(roleKey, ct); if (current is null) return false;
        var before = Context(current, current, null, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(RoleEventKind.RoleDeleting, before);
        var implicitRole = RoleContract.IsImplicitRole(roleKey);
        if (!implicitRole)
            await EachOverwriteOf(roleKey, row => RoleEventRegistry.Before(RoleEventKind.OverwriteRemoving, Removal(row, ct)), ct);

        if (current.Permissions.Count > 0)
        {
            await using var order = await EnterOrder(ct);
            var granting = Require(await RoleReads.ExactRole(roleKey, ct), roleKey);
            using (RoleMutationGuard.Allow<Role>(granting.Id, EntityLifecycleOperation.Upsert))
                current = await Copy(granting, permissions: []).Save(ct);
        }

        if (!implicitRole)
        {
            await EachOverwriteOf(roleKey, async row =>
            {
                if (row.Allow.Count == 0) return;
                if (row.Deny.Count == 0)
                {
                    await RemoveWithRole(row, ct);
                    return;
                }

                var denying = row.Duplicate();
                denying.Allow = [];
                denying.UpdatedBy = actor?.CurrentActorSubject;
                using (RoleMutationGuard.Allow<PermissionOverwrite>(denying.Id, EntityLifecycleOperation.Upsert)) await denying.Save(ct);
                _gate.Invalidate(() => bags.InvalidateOverwrite(denying));
            }, ct);
        }

        _gate.Invalidate(bags.InvalidateAll);
        for (string? after = null; ;)
        {
            var page = await Members(roleKey, after, _options.MaxPageSize, ct);
            foreach (var member in page.Items)
            {
                using (RoleMutationGuard.Allow<RoleMember>(member.Id, EntityLifecycleOperation.Remove)) await member.Remove(ct);
                _gate.Invalidate(() => bags.Invalidate(member.PersonId));
            }

            if ((after = page.Next) is null) break;
        }

        if (!implicitRole) await EachOverwriteOf(roleKey, row => RemoveWithRole(row, ct), ct);
        await using (await EnterOrder(ct))
        {
            var last = await RoleReads.ExactRole(roleKey, ct) ?? current;
            using (RoleMutationGuard.Allow<Role>(last.Id, EntityLifecycleOperation.Remove)) await last.Remove(ct);
            if (roleKey != RoleTokens.Everyone) await Shift(last.Position, null, ct);
        }

        _gate.Invalidate(bags.InvalidateAll);
        await RoleChangeGate.Published(RoleEventKind.RoleDeleted, token => before with { Phase = RoleChangePhase.After, CancellationToken = token }, ct);
        return true;
    }

    /// <summary>
    /// The overwrites that evaluate <paramref name="bag"/> on <paramref name="resourceId"/>: the resource's overwrites
    /// for the roles the bag is in, <see cref="RoleTokens.Everyone"/> included, and the bag's person's own, from the
    /// compiled cache. Each call returns its own copies; pass them to <see cref="RoleBag.On"/>, which gives the same
    /// answer it gives over every overwrite on the resource.
    /// </summary>
    public async Task<IReadOnlyList<PermissionOverwrite>> OverwritesFor(RoleBag bag, string resourceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bag);
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        var roleOverwrites = await bags.RoleOverwrites(resourceId, ct).ConfigureAwait(false);
        var rows = new List<PermissionOverwrite>();
        foreach (var role in bag.Roles)
        {
            if (roleOverwrites.TryGetValue(role, out var row)) rows.Add(row.Duplicate());
        }

        if (bag.PersonId is { } person && await bags.PersonOverwrite(resourceId, person, ct).ConfigureAwait(false) is { } own)
            rows.Add(own.Duplicate());
        return rows;
    }

    /// <summary>
    /// A page of the overwrites on <paramref name="resourceId"/>, for every subject: its role overwrites by role key, then
    /// its people's by person id; at most <paramref name="take"/> (no more than <see cref="RoleOptions.MaxPageSize"/>).
    /// Pass a page's <see cref="OverwritePage.Next"/> as <paramref name="after"/> to read the next; a page can hold fewer
    /// rows than <paramref name="take"/> while a next page follows. To evaluate a person there, read
    /// <see cref="OverwritesFor"/>.
    /// </summary>
    public async Task<OverwritePage> Overwrites(string resourceId, string? after = null, int take = 50, CancellationToken ct = default)
    {
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        RequireTake(take);
        var (fromKind, fromCursor) = ResourceCursor(after);
        var items = new List<PermissionOverwrite>(take);
        foreach (var kind in (OverwriteSubjectKind[])[OverwriteSubjectKind.Role, OverwriteSubjectKind.Person])
        {
            if (kind < fromKind) continue;
            if (items.Count == take) return new OverwritePage(items, ResourceCursor(kind, null));
            var (rows, next) = await RoleReads.Page<PermissionOverwrite>(
                row => row.ResourceId == resourceId && row.SubjectKind == kind,
                row => row.SubjectKind == kind && StringComparer.Ordinal.Equals(row.ResourceId, resourceId),
                row => row.SubjectId, row => row.SubjectId, kind == fromKind ? fromCursor : null, take - items.Count, ct);
            items.AddRange(rows);
            if (next is not null) return new OverwritePage(items, ResourceCursor(kind, next));
        }

        return new OverwritePage(items, null);
    }

    /// <summary>
    /// A page of the overwrites <paramref name="subjectId"/> holds across resources, in resource order; at most
    /// <paramref name="take"/> (no more than <see cref="RoleOptions.MaxPageSize"/>). Pass a page's
    /// <see cref="OverwritePage.Next"/> as <paramref name="after"/> to read the next.
    /// </summary>
    public async Task<OverwritePage> OverwritesOf(OverwriteSubjectKind subjectKind, string subjectId, string? after = null,
        int take = 50, CancellationToken ct = default)
    {
        subjectId = RequireSubject(subjectKind, subjectId);
        RequireTake(take);
        var (rows, next) = await RoleReads.Page<PermissionOverwrite>(
            row => row.SubjectKind == subjectKind && row.SubjectId == subjectId,
            row => row.SubjectKind == subjectKind && StringComparer.Ordinal.Equals(row.SubjectId, subjectId),
            row => row.ResourceId, row => row.ResourceId, after, take, ct);
        return new OverwritePage(rows, next);
    }

    /// <summary>
    /// Sets what <paramref name="subjectId"/> may and may not do on <paramref name="resourceId"/>, replacing its
    /// overwrite there. The subject is a role key — <see cref="RoleTokens.Everyone"/> for everyone — or a person id;
    /// a role other than the implicit ones must exist. <paramref name="allow"/> and <paramref name="deny"/> are
    /// <c>global:*</c> permissions, never <see cref="RoleTokens.Administrator"/>; a permission in both is allowed, as
    /// within one Discord step. <paramref name="expiresAt"/>, when set, is when it stops applying.
    /// </summary>
    public async Task<PermissionOverwrite> Overwrite(string resourceId, OverwriteSubjectKind subjectKind, string subjectId,
        IEnumerable<string> allow, IEnumerable<string> deny, DateTimeOffset? expiresAt = null, CancellationToken ct = default)
    {
        DemandChangeable();
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        subjectId = RequireSubject(subjectKind, subjectId);
        var allowed = NormalizeOverwritePermissions(allow, nameof(allow));
        var denied = NormalizeOverwritePermissions(deny, nameof(deny));
        if (allowed.Length == 0 && denied.Length == 0)
            throw new ArgumentException("An overwrite allows or denies at least one permission; to clear one, remove it.", nameof(allow));
        if (expiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
            throw new ArgumentException("expiresAt must be in the future; to end an overwrite now, remove it.", nameof(expiresAt));

        await using var turn = await _gate.Enter(ct);
        await using var roleGate = subjectKind == OverwriteSubjectKind.Role ? await Enter(subjectId, ct) : null;
        await using var gate = await EnterOverwrites(ct);
        if (subjectKind == OverwriteSubjectKind.Role && !RoleContract.IsImplicitRole(subjectId) && await RoleReads.ExactRole(subjectId, ct) is null)
            throw new KeyNotFoundException($"Role '{subjectId}' does not exist.");
        var id = PermissionOverwrite.KeyFor(resourceId, subjectKind, subjectId);
        var previous = await PermissionOverwrite.Get(id, ct);
        var next = new PermissionOverwrite
        {
            Id = id, ResourceId = resourceId, SubjectKind = subjectKind, SubjectId = subjectId, Allow = [.. allowed],
            Deny = [.. denied], ExpiresAt = expiresAt, UpdatedBy = actor?.CurrentActorSubject,
        };
        if (previous is not null && Equivalent(previous, next)) return previous;
        return await SaveOverwrite(previous, next, ct);
    }

    /// <summary>Removes the overwrite of <paramref name="subjectId"/> on <paramref name="resourceId"/>; false when there is none.</summary>
    public async Task<bool> RemoveOverwrite(string resourceId, OverwriteSubjectKind subjectKind, string subjectId, CancellationToken ct = default)
    {
        DemandChangeable();
        resourceId = RoleContract.Require(resourceId, nameof(resourceId));
        subjectId = RequireSubject(subjectKind, subjectId);
        await using var turn = await _gate.Enter(ct);
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
        DemandChangeable();
        if (!RoleContract.TryRequire(overwriteId, out var id) || !RoleContract.TryRequirePermission(permission, out var token)) return false;
        (overwriteId, permission) = (id, token);
        await using var turn = await _gate.Enter(ct);
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
        var saved = next;
        _gate.Invalidate(() => bags.InvalidateOverwrite(saved));
        await RoleChangeGate.Published(RoleEventKind.OverwriteChanged, token => context with { Current = saved, Phase = RoleChangePhase.After, CancellationToken = token }, ct);
        return next;
    }

    /// <summary>Removes an overwrite; the overwrite lock is held.</summary>
    private async Task RemoveOverwrite(PermissionOverwrite current, CancellationToken ct)
    {
        var context = Removal(current, ct);
        await RoleEventRegistry.Before(RoleEventKind.OverwriteRemoving, context);
        using (RoleMutationGuard.Allow<PermissionOverwrite>(current.Id, EntityLifecycleOperation.Remove)) await current.Remove(ct);
        _gate.Invalidate(() => bags.InvalidateOverwrite(current));
        await RoleChangeGate.Published(RoleEventKind.OverwriteRemoved, token => context with { Phase = RoleChangePhase.After, CancellationToken = token }, ct);
    }

    /// <summary>Removes one of a deleted role's overwrites, whose before-event already ran; the overwrite lock is held.</summary>
    private async ValueTask RemoveWithRole(PermissionOverwrite row, CancellationToken ct)
    {
        using (RoleMutationGuard.Allow<PermissionOverwrite>(row.Id, EntityLifecycleOperation.Remove)) await row.Remove(ct);
        _gate.Invalidate(() => bags.InvalidateOverwrite(row));
        var removal = Removal(row, ct);
        await RoleChangeGate.Published(RoleEventKind.OverwriteRemoved, token => removal with { Phase = RoleChangePhase.After, CancellationToken = token }, ct);
    }

    /// <summary>Visits every overwrite of a role, a page at a time; changing or removing the one visited does not move the cursor.</summary>
    private async Task EachOverwriteOf(string roleKey, Func<PermissionOverwrite, ValueTask> visit, CancellationToken ct)
    {
        for (string? after = null; ;)
        {
            var page = await OverwritesOf(OverwriteSubjectKind.Role, roleKey, after, _options.MaxPageSize, ct);
            foreach (var row in page.Items) await visit(row);
            if ((after = page.Next) is null) return;
        }
    }

    private OverwriteChangeContext Removal(PermissionOverwrite row, CancellationToken ct)
        => new(row, row, actor?.CurrentActorSubject, RoleChangePhase.Before, ct);

    /// <summary>
    /// Changes the role document under the order lock, placing it by <see cref="Placement"/> and shifting the roles
    /// between its old place and its new one. A change that can alter a bag — a role defined or moved, or its
    /// permissions changed — compiles every bag again rather than reading the role's members: such changes are rare,
    /// and a bag compiles from two bounded reads. A rename or a metadata change alters no bag.
    /// </summary>
    private async Task<Role> Mutate(string roleKey, RoleEventKind beforeKind, RoleEventKind afterKind,
        Func<Role?, Role> change, int? position, CancellationToken ct)
    {
        DemandChangeable();
        roleKey = RoleContract.Require(roleKey, nameof(roleKey));
        await using var turn = await _gate.Enter(ct);
        await using var gate = await Enter(roleKey, ct);
        await using var order = await EnterOrder(ct);
        var previous = await RoleReads.ExactRole(roleKey, ct);
        var placement = await Placement(roleKey, previous, position, ct);
        var next = change(previous);
        next.Position = placement;
        if (previous is not null && Equivalent(previous, next)) return previous;
        var context = Context(previous ?? next, next, null, RoleChangePhase.Before, ct);
        await RoleEventRegistry.Before(beforeKind, context);
        var permissionsChanged = previous is not null && !previous.Permissions.SequenceEqual(next.Permissions, StringComparer.Ordinal);
        var alsoPermissions = permissionsChanged && beforeKind == RoleEventKind.RoleChanging;
        if (alsoPermissions) await RoleEventRegistry.Before(RoleEventKind.PermissionsChanging, context);
        var placed = previous?.Position != next.Position;
        if (placed && roleKey != RoleTokens.Everyone) await Shift(previous?.Position, next.Position, ct);
        using (RoleMutationGuard.Allow<Role>(next.Id, EntityLifecycleOperation.Upsert)) next = await next.Save(ct);
        if (placed || permissionsChanged) _gate.Invalidate(bags.InvalidateAll);
        var saved = next;
        if (alsoPermissions) await RoleChangeGate.Published(RoleEventKind.PermissionsChanged, token => context with { Current = saved, Phase = RoleChangePhase.After, CancellationToken = token }, ct);
        await RoleChangeGate.Published(afterKind, token => context with { Current = saved, Phase = RoleChangePhase.After, CancellationToken = token }, ct);
        return next;
    }

    /// <summary>
    /// Where a change puts the role: <see cref="RoleTokens.Everyone"/> at 0, and a position elsewhere refused; a role
    /// that exists where it is, or at the asked position between 1 and the top; a new one at the asked position between
    /// 1 and one past the top, or else one past the top — at 1 for <see cref="RoleTokens.Authenticated"/>. The order lock
    /// is held.
    /// </summary>
    private static async Task<int> Placement(string roleKey, Role? previous, int? asked, CancellationToken ct)
    {
        if (roleKey == RoleTokens.Everyone)
            return asked is null or 0 ? 0 : throw new ArgumentException($"'{RoleTokens.Everyone}' is always at position 0, below every other role.", "position");
        if (previous is not null && asked is null) return previous.Position;
        var top = await Top(ct);
        if (previous is not null) return Math.Clamp(asked!.Value, 1, Math.Max(top, 1));
        return Math.Clamp(asked ?? (roleKey == RoleTokens.Authenticated ? 1 : top + 1), 1, top + 1);
    }

    /// <summary>The highest position a role holds, 0 when no role is above <see cref="RoleTokens.Everyone"/>: one read.</summary>
    private static async Task<int> Top(CancellationToken ct)
        => Math.Max(0, (await Role.FirstPage(1, sort => sort.OrderByDescending(role => role.Position), ct)).FirstOrDefault()?.Position ?? 0);

    /// <summary>
    /// Moves the roles between a role's old position <paramref name="from"/> and its new one <paramref name="to"/> by
    /// one, so the order is dense once the role is written or removed: a new role (no old position) moves every role at
    /// or above its position up; a role moved up moves the roles above it, up to its new position, down; a role moved
    /// down moves the roles below it, down to its new position, up; a removed role (no new position) moves every role
    /// above it down. The order lock is held.
    /// </summary>
    private Task Shift(int? from, int? to, CancellationToken ct) => (from, to) switch
    {
        (null, { } at) => ShiftRange(at, int.MaxValue, 1, ct),
        ({ } gone, null) => ShiftRange(gone + 1, int.MaxValue, -1, ct),
        ({ } old, { } at) when old < at => ShiftRange(old + 1, at, -1, ct),
        ({ } old, { } at) when old > at => ShiftRange(at, old - 1, 1, ct),
        _ => Task.CompletedTask,
    };

    /// <summary>
    /// Adds <paramref name="by"/> to the position of every role from <paramref name="low"/> to <paramref name="high"/>,
    /// a page at a time from the end the roles move toward, so a row once moved has left the range still to read. Each
    /// row is one write. Until the moving role itself is written, it can share its position with the role passing it,
    /// and the two then stand either as they stood or as they will. The order lock is held.
    /// </summary>
    private async Task ShiftRange(int low, int high, int by, CancellationToken ct)
    {
        var take = _options.MaxPageSize;
        var query = new QueryDefinition().WithSort<Role>(sort =>
        {
            if (by > 0) sort.OrderByDescending(role => role.Position);
            else sort.OrderBy(role => role.Position);
        }).WithPagination(1, take);
        while (low <= high)
        {
            var (from, to) = (low, high);
            var rows = await Role.Query(role => role.Position >= from && role.Position <= to, query, ct);
            foreach (var row in rows)
            {
                using (RoleMutationGuard.Allow<Role>(row.Id, EntityLifecycleOperation.Upsert)) await Copy(row, position: row.Position + by).Save(ct);
            }

            if (rows.Count < take) return;
            if (by > 0) high = rows[^1].Position;
            else low = rows[^1].Position;
        }
    }

    /// <summary>
    /// Refuses, before any lock is taken, a change asked for by a role lifecycle handler, a change asked for inside a
    /// deferred scope, and one inside a hold that has committed or rolled back. A role, member or overwrite change is a
    /// read-modify-write under this collection's locks, deciding from what it reads; a deferred scope has no transaction to
    /// decide in, and would hold the write until its commit while the decision went stale.
    /// </summary>
    private static void DemandChangeable()
    {
        RoleEventRegistry.DemandMutationAllowed();
        if (EntityContext.Current?.TransactionCoordinator is { } scope && RoleChangeGate.CurrentHold is null)
            throw new NotSupportedException(
                $"A role or overwrite change cannot join scope '{scope.Name}': a deferred EntityContext.Transaction scope " +
                "writes only at its commit, while a role change decides from what it reads, and a hold that has ended has " +
                "nothing left to join. Make the change inside EntityContext.Hold, or after EntityContext.Commit(), outside the scope.");
    }

    private void RequireTake(int take)
    {
        if (take < 1 || take > _options.MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(take), take, $"take must be between 1 and {_options.MaxPageSize}.");
    }

    private const char CursorSeparator = ':';

    /// <summary>A resource listing's cursor: the subject kind it is reading, and the cursor within that kind, if any.</summary>
    private static string ResourceCursor(OverwriteSubjectKind kind, string? within) => $"{kind}{CursorSeparator}{within}";

    private static (OverwriteSubjectKind Kind, string? Within) ResourceCursor(string? after)
    {
        if (string.IsNullOrWhiteSpace(after)) return (OverwriteSubjectKind.Role, null);
        var at = after.IndexOf(CursorSeparator, StringComparison.Ordinal);
        if (at < 1 || !Enum.TryParse<OverwriteSubjectKind>(after[..at], ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
            throw new ArgumentException($"'{after}' is not a cursor this listing handed back.", nameof(after));
        var within = after[(at + 1)..];
        _ = RoleReads.ParseCursor(within);
        return (kind, within.Length == 0 ? null : within);
    }

    /// <summary>The membership a read by key returned, when it names exactly this role and person.</summary>
    private static RoleMember? Exact(RoleMember? member, string roleKey, string personId)
        => member is not null && StringComparer.Ordinal.Equals(member.RoleKey, roleKey) && StringComparer.Ordinal.Equals(member.PersonId, personId)
            ? member
            : null;

    private static string Key(string bound) => $"{RoleOptions.SectionPath}:{bound}";

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
    private async Task<IAsyncDisposable> EnterOrder(CancellationToken ct) { await _orderLock.WaitAsync(ct); return new Releaser(_orderLock); }
    private async Task<IAsyncDisposable> EnterPerson(string personId, CancellationToken ct)
    {
        var stripe = _personLocks[(int)((uint)StringComparer.Ordinal.GetHashCode(personId) % PersonLockStripes)];
        await stripe.WaitAsync(ct);
        return new Releaser(stripe);
    }
    private RoleChangeContext Context(Role previous, Role current, string? subject, RoleChangePhase phase, CancellationToken ct) => new(previous, current, subject, actor?.CurrentActorSubject, phase, ct);
    private string[] NormalizePermissions(IEnumerable<string> p) => RoleContract.Normalize(p, _options.MaxPermissionsPerRole, nameof(p), allowEmpty: true);
    private static Role Require(Role? role, string key) => role ?? throw new KeyNotFoundException($"Role '{key}' does not exist.");
    private Dictionary<string, string> NormalizeMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return [];
        if (metadata.Count > _options.MaxMetadataEntries) throw new ArgumentException($"Metadata exceeds the configured bound of {_options.MaxMetadataEntries} entries.", nameof(metadata));
        return metadata.ToDictionary(entry => RoleContract.Require(entry.Key, "metadata key", 64), entry => RoleContract.RequireText(entry.Value, "metadata value", 512), StringComparer.Ordinal);
    }
    private Role Copy(Role r, string? name = null, IEnumerable<string>? permissions = null, IReadOnlyDictionary<string, string>? metadata = null, int? position = null) => new() { Id = r.Id, Key = r.Key, Name = name ?? r.Name, Permissions = [.. permissions ?? r.Permissions], Metadata = metadata?.ToDictionary() ?? r.Metadata.ToDictionary(), Position = position ?? r.Position, UpdatedBy = actor?.CurrentActorSubject };
    private static bool Equivalent(Role left, Role right) => left.Id == right.Id && left.Key == right.Key && left.Name == right.Name && left.Position == right.Position &&
        left.Permissions.SequenceEqual(right.Permissions, StringComparer.Ordinal) &&
        left.Metadata.Count == right.Metadata.Count && left.Metadata.All(entry => right.Metadata.TryGetValue(entry.Key, out var value) && value == entry.Value);
    private static bool Equivalent(PermissionOverwrite left, PermissionOverwrite right) => left.ResourceId == right.ResourceId &&
        left.SubjectKind == right.SubjectKind && left.SubjectId == right.SubjectId && left.ExpiresAt == right.ExpiresAt &&
        left.Allow.SequenceEqual(right.Allow, StringComparer.Ordinal) && left.Deny.SequenceEqual(right.Deny, StringComparer.Ordinal);
    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable { public ValueTask DisposeAsync() { semaphore.Release(); return ValueTask.CompletedTask; } }
}
