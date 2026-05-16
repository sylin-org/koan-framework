namespace Koan.Web.Auth.Services.Authentication;

public interface IServiceAuthenticator
{
    Task<string> GetServiceToken(string targetService, string[]? scopes = null, CancellationToken ct = default);
    Task<ServiceTokenInfo> GetServiceTokenInfo(string targetService, string[]? scopes = null, CancellationToken ct = default);
    Task InvalidateToken(string targetService, CancellationToken ct = default);
}

public record ServiceTokenInfo(string AccessToken, DateTimeOffset ExpiresAt, string[] GrantedScopes);