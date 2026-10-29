using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using AwesomeAssertions;
using Xunit;
using static Koan.Web.Auth.Server.IntegrationTests.OAuthTestFlows;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>
/// SEC-0023 — at the root, where Development derives the issuer from the request, a token the authorization server
/// issues through the code flow carries the issuer its metadata states, and <c>Koan.bearer</c> accepts it.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class TokenIssuerSpec : IClassFixture<OAuthFlowFixture>
{
    private readonly OAuthFlowFixture _root;

    public TokenIssuerSpec(OAuthFlowFixture root) => _root = root;

    private static async Task<HttpResponseMessage> AskBearer(HttpClient http, string pathBase, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pathBase + "/test/bearer");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    [Fact]
    public async Task At_the_root_the_code_flow_token_carries_the_metadata_issuer_and_the_bearer_accepts_it()
    {
        const string redirect = "http://127.0.0.1:7821/cb";
        await RegisterCodeClient(_root.Services, "issuer-root", redirect);
        var (verifier, challenge) = Pkce();
        using var browser = _root.NewClient();
        (await browser.GetAsync("/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var authorize = await browser.GetAsync(Authorize("", "issuer-root", redirect, challenge, _root.BaseUrl), Ct);
        var rid = Query(authorize.Headers.Location!.ToString(), "rid");
        using var approved = await Decide(browser, "", rid, "approve");
        var code = Query(approved.Headers.Location!.ToString(), "code");
        var token = await Redeem(browser, "", "issuer-root", code, verifier, redirect);

        var issuer = await MetadataIssuer(browser, "");
        issuer.Should().Be(_root.BaseUrl);
        Read(token).Issuer.Should().Be(issuer);

        using var accepted = await AskBearer(browser, "", token);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

}
