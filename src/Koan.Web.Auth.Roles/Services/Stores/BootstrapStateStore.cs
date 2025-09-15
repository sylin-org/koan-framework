using Koan.Data.Core.Model;
using Koan.Web.Auth.Roles.Contracts;

namespace Koan.Web.Auth.Roles.Services.Stores;

internal sealed class RoleBootstrapState : Entity<RoleBootstrapState>
{
    // Single row with fixed key "admin-bootstrap"
    public string? UserId { get; set; }
    public string? Mode { get; set; }
    public DateTimeOffset When { get; set; }
}

internal sealed class DefaultRoleBootstrapStateStore : IRoleBootstrapStateStore
{
    public async Task<bool> IsAdminBootstrappedAsync(CancellationToken ct = default)
    {
    var state = await RoleBootstrapState.Get("admin-bootstrap", ct).ConfigureAwait(false);
        return state is not null;
    }

    public async Task MarkAdminBootstrappedAsync(string userId, string mode, CancellationToken ct = default)
    {
        var entity = new RoleBootstrapState { Id = "admin-bootstrap", UserId = userId, Mode = mode, When = DateTimeOffset.UtcNow };
    _ = await RoleBootstrapState.UpsertMany(new[] { entity }, ct).ConfigureAwait(false);
    }
}
