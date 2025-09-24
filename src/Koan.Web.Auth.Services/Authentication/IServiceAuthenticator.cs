namespace Koan.Web.Auth.Services.Authentication;

public interface IServiceAuthenticator
{
    Task<string> GetServiceTokenAsync(string targetService, string[]? scopes = null, CancellationToken ct = default);
    Task<ServiceTokenInfo> GetServiceTokenInfoAsync(string targetService, string[]? scopes = null, CancellationToken ct = default);
    Task InvalidateTokenAsync(string targetService, CancellationToken ct = default);
}

public record ServiceTokenInfo(string AccessToken, DateTimeOffset ExpiresAt, string[] GrantedScopes);