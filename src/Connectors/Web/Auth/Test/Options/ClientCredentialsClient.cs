namespace Koan.Web.Auth.Connector.Test.Options;

public sealed class ClientCredentialsClient
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string[] AllowedScopes { get; init; } = [];
    public string Description { get; init; } = "";
}
