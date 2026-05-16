namespace Koan.Web.Auth.Domain;

public interface IUserStore
{
    Task<bool> Exists(string userId, CancellationToken ct = default);
}