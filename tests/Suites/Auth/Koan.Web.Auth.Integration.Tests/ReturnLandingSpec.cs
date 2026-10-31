using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Koan.Web.Auth.Integration.Tests;

/// <summary>
/// SEC-0029 and SEC-0027 — under <c>/tangent</c>, a sign-in round trip with the dev Test provider lands only on a local
/// path under the application: a <c>return</c> a browser would read as another host (<c>//evil.example/x</c>,
/// <c>/\evil.example</c>, <c>https://evil.example</c>, <c>%2F%2Fevil.example</c>, <c>/%2F%2Fevil.example</c>) lands on the
/// default landing <c>/tangent/</c> instead, as does a sign-in that names no return; a local path under the base is
/// kept. Logout follows the same rule and never answers 500.
/// </summary>
public sealed class ReturnLandingSpec : IClassFixture<BaseUrlAuthSwapFixture>
{
    private const string Base = BaseUrlAuthSwapFixture.PathBase;
    private readonly BaseUrlAuthSwapFixture _fx;

    public ReturnLandingSpec(BaseUrlAuthSwapFixture fx) => _fx = fx;

    public static TheoryData<string> Refused => new()
    {
        "//evil.example/x",
        @"/\evil.example",
        "https://evil.example",
        "%2F%2Fevil.example",
        "/%2F%2Fevil.example",
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_return_that_would_leave_the_application_lands_on_the_default(string value)
    {
        using var client = _fx.NewClient();
        (await SignInLanding(client, $"{Base}/auth/test/challenge?return={Uri.EscapeDataString(value)}")).Should().Be(Base + "/");

        using var logout = await client.PostAsync($"{Base}/auth/logout?return={Uri.EscapeDataString(value)}", null, TestContext.Current.CancellationToken);
        logout.StatusCode.Should().Be(HttpStatusCode.Redirect);
        logout.Headers.Location!.OriginalString.Should().Be(Base + "/");
    }

    [Fact]
    public async Task A_local_path_under_the_base_is_kept()
    {
        using var client = _fx.NewClient();
        (await SignInLanding(client, $"{Base}/auth/test/challenge?return={Base}/e2e/whoami")).Should().Be(Base + "/e2e/whoami");

        using var logout = await client.PostAsync($"{Base}/auth/logout?return={Base}/e2e/whoami", null, TestContext.Current.CancellationToken);
        logout.Headers.Location!.OriginalString.Should().Be(Base + "/e2e/whoami");
    }

    [Fact]
    public async Task No_return_lands_on_the_application_root()
    {
        using var client = _fx.NewClient();
        (await SignInLanding(client, $"{Base}/auth/test/challenge")).Should().Be(Base + "/");

        using var image = await client.GetAsync($"{Base}/auth/logout", TestContext.Current.CancellationToken);
        image.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "an image in anything a person reads must not end their session");

        using var logout = await client.PostAsync($"{Base}/auth/logout", null, TestContext.Current.CancellationToken);
        logout.Headers.Location!.OriginalString.Should().Be(Base + "/");
    }

    /// <summary>
    /// SEC-0029 §5: the path base matches without regard to case, so a browser may ask <c>/TANGENT/edge/protected</c>.
    /// After sign-in it lands back on that page, under the base in its configured spelling, where the cookie applies.
    /// </summary>
    [Fact]
    public async Task A_page_asked_with_another_casing_of_the_base_is_where_sign_in_lands()
    {
        using var client = _fx.NewClient();
        using var protectedPage = new HttpRequestMessage(HttpMethod.Get, "/TANGENT/edge/protected");
        protectedPage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/html"));
        using var login = await client.SendAsync(protectedPage, TestContext.Current.CancellationToken);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var landing = await SignInLanding(client, login.Headers.Location!.OriginalString);
        landing.Should().Be(Base + "/edge/protected");

        using var page = await client.GetAsync(landing, TestContext.Current.CancellationToken);
        page.StatusCode.Should().Be(HttpStatusCode.OK, "the person is signed in on the page they asked for");
    }

    /// <summary>Follows challenge → authorize → callback and answers where the callback sends the person.</summary>
    private async Task<string> SignInLanding(HttpClient client, string challenge)
    {
        var url = new Uri(new Uri(_fx.BaseUrl), challenge);
        var chain = new List<string>();
        for (var hop = 0; hop < 8; hop++)
        {
            if (url.AbsolutePath.EndsWith("/authorize", StringComparison.Ordinal))
                url = new Uri(QueryHelpers.AddQueryString(url.ToString(), "roles", "reader"));
            using var resp = await client.GetAsync(url, TestContext.Current.CancellationToken);
            chain.Add($"{(int)resp.StatusCode} {url.PathAndQuery}");
            resp.StatusCode.Should().Be(HttpStatusCode.Redirect, string.Join(" -> ", chain));
            var location = resp.Headers.Location!;
            if (url.AbsolutePath.EndsWith("/callback", StringComparison.Ordinal)) return location.OriginalString;
            url = location.IsAbsoluteUri ? location : new Uri(url, location);
        }
        throw new InvalidOperationException("no callback within 8 hops: " + string.Join(" -> ", chain));
    }
}
