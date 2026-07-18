# Sylin.Koan.Web.Auth.Connector.Microsoft — technical contract

The module registers one non-automatic OIDC `AuthProviderDefinition`: ID `microsoft`, priority `200`, authority
`https://login.microsoftonline.com/common/v2.0`, and scopes `openid`, `email`, `profile`.

Web Auth overlays `Koan:Web:Auth:Providers:microsoft`, requires `ClientId` and `ClientSecret`, and realizes the
provider with `OpenIdConnectHandler`. Challenge is `/auth/microsoft/challenge`; callback is
`/auth/microsoft/callback`.

Set an explicit tenant authority when accepting every tenant represented by `common` is not the application's
policy. Provider tokens and Microsoft Graph access are outside the connector's current contract.

The connector contains no route, handler, election, or startup-order machinery. Web Auth's single provider plan owns
all runtime decisions and evidence.
