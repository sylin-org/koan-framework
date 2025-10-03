namespace Koan.Web.Auth.Connector.Test.Options;

public sealed class ClientCredentialsClient
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string[] AllowedScopes { get; init; } = Array.Empty<string>();
    public string Description { get; init; } = string.Empty;
}
