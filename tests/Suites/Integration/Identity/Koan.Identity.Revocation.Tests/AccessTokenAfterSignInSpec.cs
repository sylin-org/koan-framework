using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Core;
using System.Security.Cryptography;
using System.Text;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Server.Protocol;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using System.IdentityModel.Tokens.Jwt;

namespace Koan.Identity.Revocation.Tests;

/// <summary>A door where an account proved itself some other way, on a provider's callback path so that a link can settle
/// it on another person. It carries <c>sub</c> as a provider's principal does.</summary>
public sealed class ProofDoorController : ControllerBase
{
    public const string Provider = "door";

    [HttpPost("/auth/door/callback")]
    public async Task<IActionResult> Prove([FromQuery] string subject, [FromQuery] bool signIn, CancellationToken ct)
    {
        var person = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, subject), new Claim("sub", subject)], Provider));
        if (signIn) await HttpContext.SignInAsync(AuthenticationExtensions.CookieScheme, person);
        var token = await AccessToken.For(HttpContext, person, ct);
        return token is null ? StatusCode(403) : Ok(new { token = token.Value });
    }
}

/// <summary>
/// SEC-0037 on a host with Koan.Identity, whose standing knows only those who have signed in: a door signs in first, and passes
/// the principal it signed in, which sign-in settled on the person's id.
/// </summary>
public sealed class AccessTokenAfterSignInSpec(RevocationFixture fixture) : IClassFixture<RevocationFixture>
{
    private static string Account() => $"did:plc:{Guid.CreateVersion7():N}";

    private async Task<HttpResponseMessage> Prove(string subject, bool signIn)
    {
        using var client = fixture.Browser();
        return await client.PostAsync($"/auth/door/callback?subject={Uri.EscapeDataString(subject)}&signIn={signIn}", null, RevocationFixture.Ct);
    }

    private static async Task<string> SubjectOf(HttpResponseMessage answer)
    {
        answer.StatusCode.Should().Be(HttpStatusCode.OK, await answer.Content.ReadAsStringAsync(RevocationFixture.Ct));
        using var document = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(RevocationFixture.Ct));
        return new JwtSecurityTokenHandler().ReadJwtToken(document.RootElement.GetProperty("token").GetString()).Subject;
    }

    [Fact]
    public async Task A_new_account_gets_no_token_until_it_has_signed_in()
    {
        var account = Account();

        using var before = await Prove(account, signIn: false);
        using var after = await Prove(account, signIn: true);

        before.StatusCode.Should().Be(HttpStatusCode.Forbidden, "standing knows no one who has not signed in");
        (await SubjectOf(after)).Should().Be(account);
    }

    [Fact]
    public async Task An_account_linked_to_a_person_gets_that_persons_token()
    {
        var account = Account();
        var person = $"person:{Guid.CreateVersion7():N}";
        var keyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)));
        using (AppHost.PushScope(fixture.Services))
        {
            await new Identity { Id = person }.Save(RevocationFixture.Ct);
            await new ExternalIdentityLink
            {
                Id = ExternalIdentityLink.KeyFor(account, ProofDoorController.Provider, keyHash),
                IdentityId = person,
                Provider = ProofDoorController.Provider,
                ProviderKeyHash = keyHash,
            }.Save(RevocationFixture.Ct);
        }

        using var answer = await Prove(account, signIn: true);

        (await SubjectOf(answer)).Should().Be(person, "sign-in settled the account on the person");
    }
}
