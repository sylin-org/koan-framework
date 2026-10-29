using System.Collections.Generic;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — a base URL whose path is the authorization server's own first segment (<c>/oauth</c>), with the consent
/// pages at <c>/consent</c> and a configuration-only OAuth2 provider, <c>corp</c>, whose challenge link the request
/// projection carries.
/// </summary>
public sealed class OAuthBasePathFixture : BasePathFixture
{
    public override string ApplicationUrl => "https://www.example.com/oauth";

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Koan:Web:Auth:Server:ConsentPath"] = "/consent",
        ["Koan:Web:Auth:Server:DonePath"] = "/consent/done",
        ["Koan:Web:Auth:Providers:corp:Type"] = "oauth2",
        ["Koan:Web:Auth:Providers:corp:AuthorizationEndpoint"] = "https://idp.example/authorize",
        ["Koan:Web:Auth:Providers:corp:TokenEndpoint"] = "https://idp.example/token",
        ["Koan:Web:Auth:Providers:corp:UserInfoEndpoint"] = "https://idp.example/userinfo",
        ["Koan:Web:Auth:Providers:corp:ClientId"] = "koan",
        ["Koan:Web:Auth:Providers:corp:ClientSecret"] = "not-a-secret",
    };
}
