namespace Koan.Web.Auth.Domain;

public interface IUserStore
{
    Task<bool> ExistsAsync(string userId, CancellationToken ct = default);
}