using System.Collections.Generic;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — a base URL whose path is Web Auth's own first segment (<c>/auth</c>), with the consent pages at
/// <c>/consent</c> and a configuration-only OAuth2 provider, <c>corp</c>.
/// </summary>
public sealed class AuthBasePathFixture : BasePathFixture
{
    public override string ApplicationUrl => "https://www.example.com/auth";

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
