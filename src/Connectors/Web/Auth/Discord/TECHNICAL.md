# Sylin.Koan.Web.Auth.Connector.Discord — technical contract

The module registers one non-automatic OAuth2 `AuthProviderDefinition`: ID `discord`, priority `150`, Discord's
authorization/token/userinfo endpoints, and scopes `identify`, `email`.

Web Auth overlays `Koan:Web:Auth:Providers:discord`, requires `ClientId` and `ClientSecret`, and realizes the provider
with ASP.NET's `OAuthHandler<OAuthOptions>`. Challenge is `/auth/discord/challenge`; callback is
`/auth/discord/callback`. The handler uses authorization code plus PKCE and maps Discord `id` to the application
subject.

The connector has no route mapping, static registry, provider election, or middleware. Guild-aware authorization and
calling Discord APIs require application-owned scopes and token handling beyond this package.
