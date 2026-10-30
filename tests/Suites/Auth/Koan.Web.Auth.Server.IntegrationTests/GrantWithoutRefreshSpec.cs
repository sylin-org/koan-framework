using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Authorization;
using Xunit;

namespace Koan.Web.Auth.Server.IntegrationTests;

/// <summary>A flow host with refresh tokens turned off, on its own in-memory store.</summary>
public sealed class NoRefreshFlowFixture : OAuthFlowFixture
{
    protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
    [
        new("Koan:Data:Sources:Default:ConnectionString", "memory://oauth-no-refresh-tests"),
        new("Koan:Web:Auth:Server:EnableRefreshTokens", "false"),
    ];
}

/// <summary>
/// SEC-0005 (amended 2026-10-30) — with refresh tokens off, a connection has nothing a grant could back: the token
/// endpoint issues no grant, so nothing is listed that removing would not end, and no consent is remembered.
/// </summary>
[Collection(AuthServerHostCollection.Name)]
public sealed class GrantWithoutRefreshSpec : IClassFixture<NoRefreshFlowFixture>
{
    private readonly NoRefreshFlowFixture _fx;
    public GrantWithoutRefreshSpec(NoRefreshFlowFixture fx) => _fx = fx;

    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
    private const string Redirect = "http://127.0.0.1:7790/cb";

    [Fact]
    public async Task Without_refresh_tokens_a_connection_issues_no_grant_and_remembers_no_consent()
    {
        const string person = "no-refresh-person";
        await new OAuthClient { Id = "no-refresh", ClientName = "Short Client", RedirectUris = [Redirect], CreatedUtc = DateTimeOffset.UtcNow }.Save(Ct);
        using var browser = _fx.NewClient();
        (await browser.GetAsync($"/test/signin?sub={person}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var connection = 0; connection < 2; connection++)
        {
            var (verifier, challenge) = OAuthTestFlows.Pkce();
            var authorize = await browser.GetAsync(OAuthTestFlows.Authorize("", "no-refresh", Redirect, challenge, $"{_fx.BaseUrl}/mcp"), Ct);
            authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
            var location = authorize.Headers.Location!.ToString();
            location.Should().NotStartWith(Redirect, "every connection asks the person");

            using var approve = new HttpRequestMessage(HttpMethod.Post, $"/oauth/request/{OAuthTestFlows.Query(location, "rid")}/approve");
            approve.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var approved = JsonDocument.Parse(await (await browser.SendAsync(approve, Ct)).Content.ReadAsStringAsync(Ct));
            var code = OAuthTestFlows.Query(approved.RootElement.GetProperty("redirect").GetString()!, "code");

            using var token = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = OAuthGrantTypes.AuthorizationCode,
                ["code"] = code,
                ["redirect_uri"] = Redirect,
                ["client_id"] = "no-refresh",
                ["code_verifier"] = verifier,
            }), Ct);
            token.StatusCode.Should().Be(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await token.Content.ReadAsStringAsync(Ct));
            body.RootElement.TryGetProperty("refresh_token", out _).Should().BeFalse();
        }

        (await AgentGrant.Query(g => g.Subject == person, Ct)).Should().BeEmpty("no grant is issued that nothing references");
    }
}
