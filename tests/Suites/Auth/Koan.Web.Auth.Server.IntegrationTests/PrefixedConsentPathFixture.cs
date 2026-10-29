using System.Collections.Generic;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0027 — a base URL under <c>/notes</c> whose consent and done paths were configured with the path base already in
/// them; they are application-relative, so the server writes <c>/notes/notes/consent</c> everywhere.
/// </summary>
public sealed class PrefixedConsentPathFixture : BasePathFixture
{
    public override string ApplicationUrl => "https://www.example.com/notes";

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Koan:Web:Auth:Server:ConsentPath"] = "/notes/consent",
        ["Koan:Web:Auth:Server:DonePath"] = "/notes/consent/done",
    };
}
