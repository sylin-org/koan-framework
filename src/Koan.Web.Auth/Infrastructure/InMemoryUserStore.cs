using Koan.Web.Auth.Domain;
using System.Collections.Concurrent;

namespace Koan.Web.Auth.Infrastructure;

internal sealed class InMemoryUserStore : IUserStore
{
    private readonly ConcurrentDictionary<string, byte> _users = new();
    public Task<bool> ExistsAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(_users.ContainsKey(userId));
}