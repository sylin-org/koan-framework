using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

internal sealed class ScopedRoleSnapshotCache : IScopedRoleAccessInvalidator
{
    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _entries = [];
    private readonly LinkedList<CacheEntry> _lru = [];
    private readonly Dictionary<DomainVersionKey, long> _domainVersions = [];
    private readonly int _capacity;
    private readonly int _domainVersionCapacity;
    private readonly TimeProvider _clock;
    private long _buildCount;
    private long _membershipVersion = DateTimeOffset.UtcNow.UtcTicks;

    public ScopedRoleSnapshotCache(IOptions<RoleEngineOptions> options)
        : this(options.Value, TimeProvider.System) { }

    internal ScopedRoleSnapshotCache(RoleEngineOptions options, TimeProvider clock)
    {
        _capacity = options.MaxCompiledSnapshots;
        _domainVersionCapacity = options.MaxDomainVersions;
        _clock = clock;
    }

    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal long BuildCount => Interlocked.Read(ref _buildCount);
    internal long NextMembershipVersion() => Interlocked.Increment(ref _membershipVersion);

    internal async Task<CompiledSnapshot> Get(CacheKey key,
        Func<CancellationToken, Task<CompiledSnapshot>> build, CancellationToken ct)
    {
        while (true)
        {
            CacheEntry entry;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var node))
                {
                    _lru.Remove(node);
                    _lru.AddLast(node);
                    entry = node.Value;
                }
                else
                {
                    entry = new(key, new(async () =>
                    {
                        Interlocked.Increment(ref _buildCount);
                        return await build(ct).ConfigureAwait(false);
                    }, LazyThreadSafetyMode.ExecutionAndPublication));
                    _entries.Add(key, _lru.AddLast(entry));
                    while (_entries.Count > _capacity && _lru.First is { } oldest)
                        RemoveUnderLock(oldest.Value);
                }
            }

            CompiledSnapshot snapshot;
            try { snapshot = await entry.Value.Value.WaitAsync(ct).ConfigureAwait(false); }
            catch { Remove(entry); throw; }
            // An invalidation can race a build. Never publish that completed-but-evicted
            // snapshot to the caller; loop so the caller observes the replacement generation.
            if (!IsCurrent(entry)) continue;
            if (snapshot.ValidUntil is not { } expiry || expiry > _clock.GetUtcNow()) return snapshot;
            Remove(entry);
        }
    }

    internal void InvalidateScope(ScopedRoleScopeRef scope) => Invalidate(scope, null);

    internal void InvalidateRole(string tenantId, string roleId)
    {
        tenantId = ScopedRoleScopeRef.Require(tenantId, nameof(tenantId));
        roleId = ScopedRoleScopeRef.Require(roleId, nameof(roleId));
        InvalidateWhere(key => key.TenantId == tenantId, snapshot => snapshot.DependencyRoleIds.Contains(roleId));
    }

    internal void InvalidateBinding(ScopedRoleBinding binding) => Invalidate(binding.Scope(), null);
    internal void InvalidatePolicy(ScopedRoleScopeRef scope) => Invalidate(scope, null);

    public void Invalidate(ScopedRoleDomainVersionChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var scope = change.Scope.Normalize();
        var versionKey = ScopedRoleScopeRef.Require(change.VersionKey, nameof(change.VersionKey));
        if (change.Version < 0) throw new ArgumentOutOfRangeException(nameof(change));
        var key = new DomainVersionKey(scope, versionKey);
        lock (_gate)
        {
            if (_domainVersions.TryGetValue(key, out var current) && current >= change.Version) return;
            if (!_domainVersions.ContainsKey(key) && _domainVersions.Count >= _domainVersionCapacity)
            {
                _domainVersions.Clear();
                _entries.Clear();
                _lru.Clear();
            }
            _domainVersions[key] = change.Version;
            InvalidateUnderLock(cacheKey => cacheKey.TenantId == scope.TenantId,
                snapshot => ContainsScope(snapshot.Ancestry, scope));
        }
    }

    private void Invalidate(ScopedRoleScopeRef scope, string? _)
    {
        scope = scope.Normalize();
        InvalidateWhere(key => key.TenantId == scope.TenantId,
            snapshot => ContainsScope(snapshot.Ancestry, scope));
    }

    private void InvalidateWhere(Func<CacheKey, bool> candidate, Func<CompiledSnapshot, bool> affected)
    {
        lock (_gate) InvalidateUnderLock(candidate, affected);
    }

    private void InvalidateUnderLock(Func<CacheKey, bool> candidate, Func<CompiledSnapshot, bool> affected)
    {
        foreach (var node in _entries.Values.Where(node => candidate(node.Value.Key)).ToArray())
        {
            var task = node.Value.Value.IsValueCreated ? node.Value.Value.Value : null;
            if (task is null || !task.IsCompletedSuccessfully || affected(task.Result)) RemoveUnderLock(node.Value);
        }
    }

    private void Remove(CacheEntry entry) { lock (_gate) RemoveUnderLock(entry); }

    private bool IsCurrent(CacheEntry entry)
    {
        lock (_gate)
            return _entries.TryGetValue(entry.Key, out var node) && ReferenceEquals(node.Value, entry);
    }

    private void RemoveUnderLock(CacheEntry entry)
    {
        if (!_entries.TryGetValue(entry.Key, out var node) || !ReferenceEquals(node.Value, entry)) return;
        _entries.Remove(entry.Key);
        _lru.Remove(node);
    }

    private static bool ContainsScope(IEnumerable<ScopedRoleScopeRef> ancestry, ScopedRoleScopeRef scope)
        => ancestry.Any(item => item.TenantId == scope.TenantId && item.Type == scope.Type && item.Id == scope.Id);

    internal readonly record struct CacheKey(string TenantId, string ScopeType, string ScopeId);
    internal sealed record CompiledPolicy(string Id, IReadOnlyList<ScopedRoleAudienceClause> Audience,
        IReadOnlyDictionary<string, IReadOnlySet<string>> RoleMembers, IReadOnlySet<string> CandidateSubjects,
        ScopedRoleAudience Predicate);
    internal sealed record CompiledSnapshot(CacheKey Key, IReadOnlyList<ScopedRoleScopeRef> Ancestry,
        IReadOnlyDictionary<string, IReadOnlyList<ScopedRoleGrantClause>> RoleGrants,
        IReadOnlyDictionary<string, IReadOnlySet<string>> RoleMembers,
        IReadOnlyDictionary<string, IReadOnlySet<string>> SubjectRoles,
        IReadOnlyDictionary<string, IReadOnlySet<string>> SubjectCapabilities,
        IReadOnlyDictionary<string, ScopedRoleMembershipSet> SubjectMemberships,
        IReadOnlyDictionary<string, ScopedRoleAudience> DefaultAudiences,
        IReadOnlyDictionary<string, CompiledPolicy> Policies, IReadOnlySet<string> DependencyRoleIds,
        IReadOnlyDictionary<string, long> ScopeVersions,
        IReadOnlyDictionary<string, long> RoleVersions,
        IReadOnlyDictionary<string, long> PolicyVersions,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> SubjectBindingVersions,
        DateTimeOffset? ValidUntil);
    private sealed record CacheEntry(CacheKey Key, Lazy<Task<CompiledSnapshot>> Value);
    private sealed record DomainVersionKey(ScopedRoleScopeRef Scope, string VersionKey);
}
