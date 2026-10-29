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
/// SEC-0023 — under a base URL with a path, a token the authorization server issues through the device flow carries
/// the issuer its metadata states and <c>Koan.bearer</c> accepts it; a token signed by the same key under another
/// issuer is refused.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class TokenIssuerUnderPathSpec : IClassFixture<BaseUrlPathFixture>
{
    private readonly BaseUrlPathFixture _path;

    public TokenIssuerUnderPathSpec(BaseUrlPathFixture path) => _path = path;

    private static async Task<HttpResponseMessage> AskBearer(HttpClient http, string pathBase, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pathBase + "/test/bearer");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Under_a_path_the_device_flow_token_carries_the_metadata_issuer_and_the_bearer_accepts_it()
    {
        await RegisterDeviceClient(_path.Services, "issuer-path");
        using var device = _path.NewClient();
        var (deviceCode, userCode) = await StartDevice(device, BaseUrlPathFixture.PathBase, "issuer-path", BaseUrlPathFixture.ApplicationUrl);

        using var browser = _path.NewCookieClient();
        (await browser.GetAsync($"{BaseUrlPathFixture.PathBase}/test/signin", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Decide(browser, BaseUrlPathFixture.PathBase, userCode, "approve")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var token = await PollForToken(device, BaseUrlPathFixture.PathBase, "issuer-path", deviceCode);

        var issuer = await MetadataIssuer(device, BaseUrlPathFixture.PathBase);
        issuer.Should().Be(BaseUrlPathFixture.ApplicationUrl);
        Read(token).Issuer.Should().Be(issuer);

        using var accepted = await AskBearer(device, BaseUrlPathFixture.PathBase, token);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_bearer_refuses_a_token_signed_by_the_same_key_under_another_issuer()
    {
        using var http = _path.NewClient();
        var foreign = Sign(_path.Services, issuer: "https://elsewhere.example", audience: BaseUrlPathFixture.ApplicationUrl);
        var own = Sign(_path.Services, issuer: BaseUrlPathFixture.ApplicationUrl, audience: BaseUrlPathFixture.ApplicationUrl);

        using var refused = await AskBearer(http, BaseUrlPathFixture.PathBase, foreign);
        using var accepted = await AskBearer(http, BaseUrlPathFixture.PathBase, own);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK, "the same key under the application's issuer is accepted");
    }
}
