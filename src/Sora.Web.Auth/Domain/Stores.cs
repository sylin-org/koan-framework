namespace Sora.Web.Auth.Domain;

public interface IUserStore
{
    Task<bool> ExistsAsync(string userId, CancellationToken ct = default);
}

public interface IExternalIdentityStore
{
    Task<IReadOnlyList<ExternalIdentity>> GetByUserAsync(string userId, CancellationToken ct = default);
    Task LinkAsync(ExternalIdentity identity, CancellationToken ct = default);
    Task UnlinkAsync(string userId, string provider, string providerKeyHash, CancellationToken ct = default);
}
