namespace Koan.Web.Auth.Roles.Contracts;

// Tracks one-time admin bootstrap completion and optional metadata
public interface IRoleBootstrapStateStore
{
    Task<bool> IsAdminBootstrapped(CancellationToken ct = default);
    Task MarkAdminBootstrapped(string userId, string mode, CancellationToken ct = default);
}
