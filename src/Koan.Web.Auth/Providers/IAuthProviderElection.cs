namespace Koan.Web.Auth.Providers;

public interface IAuthProviderElection
{
    AuthProviderSelection Current { get; }
}
