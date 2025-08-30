namespace Sora.Web.Auth.Roles.Contracts;

// Tracks one-time admin bootstrap completion and optional metadata
public interface IRoleBootstrapStateStore
{
    Task<bool> IsAdminBootstrappedAsync(CancellationToken ct = default);
    Task MarkAdminBootstrappedAsync(string userId, string mode, CancellationToken ct = default);
}
