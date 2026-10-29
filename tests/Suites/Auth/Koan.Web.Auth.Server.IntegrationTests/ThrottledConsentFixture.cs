namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0028 — <see cref="ConsentPageFixture"/> with an allowance of two user-code attempts per address per minute, so a
/// spec can spend it.
/// </summary>
public sealed class ThrottledConsentFixture : ConsentPageFixture
{
    protected override int UserCodeLimit => 2;
}
