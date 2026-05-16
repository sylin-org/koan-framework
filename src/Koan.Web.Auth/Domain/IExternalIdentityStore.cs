namespace Koan.Web.Auth.Domain;

public interface IExternalIdentityStore
{
    Task<IReadOnlyList<ExternalIdentity>> GetByUser(string userId, CancellationToken ct = default);
    Task Link(ExternalIdentity identity, CancellationToken ct = default);
    Task Unlink(string userId, string provider, string providerKeyHash, CancellationToken ct = default);
}