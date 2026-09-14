using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Roles;

public sealed class RoleBagCache(IOptions<RoleOptions> options)
{
    private readonly RoleOptions _options = RoleContract.Validate(options.Value);
    private readonly ConcurrentDictionary<string, CacheEntry> _bags = new(StringComparer.Ordinal);

    public async Task<RoleBag> Get(string personId, CancellationToken ct)
    {
        personId = RoleContract.Require(personId, nameof(personId));
        while (true)
        {
            if (_bags.Count >= _options.MaxCachedBags && !_bags.ContainsKey(personId)) _bags.Clear();
            var entry = _bags.GetOrAdd(personId, id => Entry(id, 0));
            try
            {
                var bag = await entry.Value.Value.WaitAsync(ct).ConfigureAwait(false);
                if (_bags.TryGetValue(personId, out var current) && ReferenceEquals(current, entry)) return bag;
            }
            catch { _bags.TryRemove(new KeyValuePair<string, CacheEntry>(personId, entry)); throw; }
            _bags.TryRemove(new KeyValuePair<string, CacheEntry>(personId, entry));
        }
    }
    public void Invalidate(string personId)
    {
        while (_bags.TryGetValue(personId, out var current) &&
               !_bags.TryUpdate(personId, Entry(personId, current.Generation + 1), current)) { }
    }
    public void Invalidate(IEnumerable<string> people) { foreach (var person in people.Distinct(StringComparer.Ordinal)) Invalidate(person); }

    private async Task<RoleBag> Compile(string personId, CancellationToken ct)
    {
        var query = new QueryDefinition { Page = 1, PageSize = _options.MaxRolesPerPerson + 1 };
        var roles = await Role.Query(role => role.Members.Contains(personId), query, ct).ConfigureAwait(false);
        if (roles.Count > _options.MaxRolesPerPerson) throw new InvalidOperationException($"Person role count exceeds the configured bound of {_options.MaxRolesPerPerson}.");
        var tokens = roles.Select(role => role.Id)
            .Concat(roles.SelectMany(role => role.Permissions.Where(permission => permission.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal))));
        return new RoleBag(personId, true, tokens);
    }
    private CacheEntry Entry(string personId, long generation) => new(generation,
        new Lazy<Task<RoleBag>>(() => Compile(personId, CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));
    private sealed record CacheEntry(long Generation, Lazy<Task<RoleBag>> Value);
}
