using System.Collections.Generic;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — a base URL under <c>/me</c> with the default consent and done paths (<c>/me/connect</c>), which are
/// application-relative, so the server writes <c>/me/me/connect</c> everywhere.
/// </summary>
public sealed class MeBasePathFixture : BasePathFixture
{
    public override string ApplicationUrl => "https://www.example.com/me";
}
