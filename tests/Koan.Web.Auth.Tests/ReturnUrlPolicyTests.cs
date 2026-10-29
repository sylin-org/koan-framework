using AwesomeAssertions;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>
/// SEC-0001 §10: the allow-list is the security boundary and is meant to permit absolute (cross-origin)
/// return URLs. ReturnUrlPolicy must therefore resolve such a URL AND report it as non-local, so the
/// controller issues a plain Redirect instead of LocalRedirect (the original 500 bug).
/// </summary>
public sealed class ReturnUrlPolicyTests
{
    private static readonly string[] AllowList = { "http://myhost.test/", "https://partner.example.com/cb" };

    [Fact]
    public void Relative_path_is_kept_and_is_local()
    {
        var r = ReturnUrlPolicy.Resolve("/dashboard", AllowList, "/");
        r.Url.Should().Be("/dashboard");
        r.IsLocal.Should().BeTrue();
    }

    [Fact]
    public void Allowlisted_absolute_is_kept_and_is_not_local()
    {
        // The exact repro: an allow-listed absolute URL must survive AND be flagged non-local
        // so the callback uses Redirect (LocalRedirect would throw "URL is not local" → 500).
        var r = ReturnUrlPolicy.Resolve("http://myhost.test/", AllowList, "/");
        r.Url.Should().Be("http://myhost.test/");
        r.IsLocal.Should().BeFalse();
    }

    [Fact]
    public void Non_allowlisted_absolute_falls_back_to_local_default()
    {
        var r = ReturnUrlPolicy.Resolve("http://evil.example/", AllowList, "/");
        r.Url.Should().Be("/");
        r.IsLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_candidate_falls_back_to_default(string? candidate)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, AllowList, "/home");
        r.Url.Should().Be("/home");
        r.IsLocal.Should().BeTrue();
    }

    [Fact]
    public void Allowlist_match_ignores_the_case_of_scheme_and_host_and_redirects_to_the_parsed_url()
    {
        var r = ReturnUrlPolicy.Resolve("HTTP://MyHost.test/page?x=1", AllowList, "/");
        r.Url.Should().Be("http://myhost.test/page?x=1");
        r.IsLocal.Should().BeFalse();
    }

    /// <summary>
    /// SEC-0029 §2: an entry names an origin, so a URL that merely starts with the entry's text is another origin — a
    /// longer host, userinfo that puts the real host after <c>@</c> (encoded or not), another port — and falls back.
    /// </summary>
    [Theory]
    [InlineData("https://app.example.evil.com/")]
    [InlineData("https://app.example@evil.com/")]
    [InlineData("https://app.example%2F@evil.com/")]
    [InlineData("https://app.example:8443/")]
    [InlineData("http://app.example/")]
    [InlineData("https://user@app.example/")]
    public void A_look_alike_of_an_allow_listed_origin_is_refused(string candidate)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, ["https://app.example"], "/");
        r.Url.Should().Be("/");
        r.IsLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData("https://app.example", "https://app.example/")]
    [InlineData("https://app.example/dashboard?tab=1#top", "https://app.example/dashboard?tab=1#top")]
    [InlineData("https://APP.example:443/x", "https://app.example/x")]
    public void A_return_on_the_exact_allow_listed_origin_is_kept(string candidate, string expected)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, ["https://app.example"], "/");
        r.Url.Should().Be(expected);
        r.IsLocal.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://partner.example.com/cb", true)]
    [InlineData("https://partner.example.com/cb/done?x=1", true)]
    [InlineData("https://partner.example.com/cbx", false)]
    [InlineData("https://partner.example.com/other", false)]
    [InlineData("https://partner.example.com/cb/../admin", false)]
    [InlineData("https://partner.example.com/cb%2F..%2Fadmin", false)]
    [InlineData("https://partner.example.com/cb/..%2Fadmin", false)]
    [InlineData("https://partner.example.com/cb/%2E%2E%2Fadmin", false)]
    [InlineData("https://partner.example.com/cb/%252E%252E%252Fadmin", false)]
    [InlineData("https://partner.example.com/cb/a%5Cb", false)]
    [InlineData("https://partner.example.com/cb/x%20y", true)]
    public void An_entry_with_a_path_admits_only_paths_under_it_at_a_segment_boundary(string candidate, bool kept)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, AllowList, "/");
        (r.Url != "/").Should().Be(kept);
    }

    /// <summary>
    /// SEC-0029 §2: a host spelled in Unicode that IDNA maps to an allow-listed host (full-width letters here) is that
    /// host, and the redirect names it in the ASCII form that was compared, so the <c>Location</c> header is valid.
    /// </summary>
    [Theory]
    [InlineData("https://ａｐｐ.example/x", "https://app.example/x")]
    [InlineData("https://ａｐｐ.example/caf%C3%A9?q=%C3%A9#top", "https://app.example/caf%C3%A9?q=%C3%A9#top")]
    public void A_unicode_spelling_of_an_allowed_host_redirects_to_the_ascii_form_compared(string candidate, string expected)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, ["https://app.example"], "/");
        r.Url.Should().Be(expected);
        r.Url.Should().MatchRegex("^[!-~]+$", "a Location header is printable ASCII");
        r.IsLocal.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://app.example", true)]
    [InlineData("https://app.example/", true)]
    [InlineData("https://partner.example.com/cb", true)]
    [InlineData("http://localhost:5220", true)]
    [InlineData("app.example", false)]
    [InlineData("/cb", false)]
    [InlineData("ftp://app.example", false)]
    [InlineData("https://user@app.example", false)]
    [InlineData("https://app.example?next=1", false)]
    [InlineData("https://app.example/#top", false)]
    [InlineData("", false)]
    public void An_allow_list_entry_is_an_absolute_http_origin_with_an_optional_path(string entry, bool valid)
    {
        ReturnUrlPolicy.IsAllowListEntry(entry).Should().Be(valid);
    }

    /// <summary>SEC-0029 §3: the default landing is a local path, or an absolute URL under an allow-list entry.</summary>
    [Theory]
    [InlineData("/", true)]
    [InlineData("/home", true)]
    [InlineData("home", true)]
    [InlineData("/home?welcome=1", true)]
    [InlineData("https://partner.example.com/cb/landing", true)]
    [InlineData("//evil.example", false)]
    [InlineData(@"/\evil.example", false)]
    [InlineData("/%2F%2Fevil.example", false)]
    [InlineData("/a/../b", false)]
    [InlineData("https://evil.example/", false)]
    [InlineData("https://partner.example.com.evil.com/cb", false)]
    public void A_default_path_is_a_local_path_or_an_allow_listed_url(string defaultPath, bool valid)
    {
        ReturnUrlPolicy.AcceptsDefaultPath(defaultPath, AllowList).Should().Be(valid);
    }

    [Fact]
    public void Empty_allowlist_rejects_all_absolutes()
    {
        var r = ReturnUrlPolicy.Resolve("http://myhost.test/", System.Array.Empty<string>(), "/");
        r.Url.Should().Be("/");
        r.IsLocal.Should().BeTrue();
    }

    /// <summary>
    /// SEC-0029: a value a browser reads as another host, or one that leaves the application, is never a landing; it
    /// falls back to the default landing.
    /// </summary>
    [Theory]
    [InlineData("//evil.example/x")]
    [InlineData(@"/\evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("%2F%2Fevil.example")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/%252F%252Fevil.example")]
    [InlineData("/%5Cevil.example")]
    [InlineData("/\t/evil.example")]
    [InlineData("/notes/../evil")]
    [InlineData("/notes/%2e%2e/evil")]
    [InlineData("/other/x")]
    [InlineData("/notesy/x")]
    [InlineData("javascript:alert(1)")]
    public void A_value_that_could_leave_the_application_is_refused(string candidate)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, AllowList, "/notes/", new PathString("/notes"));
        r.Url.Should().Be("/notes/");
        r.IsLocal.Should().BeTrue();
    }

    [Theory]
    [InlineData("/notes")]
    [InlineData("/notes/")]
    [InlineData("/notes/topics/a%20b?from=home#top")]
    public void A_local_path_under_the_base_is_kept(string candidate)
    {
        var r = ReturnUrlPolicy.Resolve(candidate, AllowList, "/notes/", new PathString("/notes"));
        r.Url.Should().Be(candidate);
        r.IsLocal.Should().BeTrue();
    }

    /// <summary>SEC-0027: the default landing is application-relative and gets the configured base exactly once.</summary>
    [Theory]
    [InlineData("/", "/notes/")]
    [InlineData("/notes", "/notes/notes")]
    [InlineData("home", "/notes/home")]
    [InlineData("https://partner.example.com/cb", "https://partner.example.com/cb")]
    [InlineData("https://ｐａｒｔｎｅｒ.example.com/cb", "https://partner.example.com/cb")]
    public void The_default_landing_gets_the_base_once(string defaultPath, string expected)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(new KoanWebBaseUrl("https://example.test/notes"))
                .BuildServiceProvider(),
        };

        ReturnUrlPolicy.DefaultLanding(context, defaultPath).Should().Be(expected);
        ReturnUrlPolicy.Resolve(context, "//evil.example", new ReturnUrlOptions { DefaultPath = defaultPath }).Url.Should().Be(expected);
    }

    /// <summary>
    /// WEB-0078: a URL Koan builds carries the configured path base unless it begins with that exact path base,
    /// compared ordinally at a segment boundary, because a proxy may route the path base case-sensitively.
    /// </summary>
    [Theory]
    [InlineData("/sign-in", "/tangent/sign-in")]
    [InlineData("/tangent", "/tangent")]
    [InlineData("/tangent/sign-in", "/tangent/sign-in")]
    [InlineData("/tangent?from=home", "/tangent?from=home")]
    [InlineData("/TANGENT/x", "/tangent/TANGENT/x")]
    [InlineData("/Tangent", "/tangent/Tangent")]
    [InlineData("/tangential/x", "/tangent/tangential/x")]
    [InlineData("https://idp.example/token", "https://idp.example/token")]
    [InlineData("sign-in", "sign-in")]
    public void A_built_url_is_placed_under_the_exact_path_base(string url, string expected)
    {
        ReturnUrlPolicy.UnderPathBase(url, new Microsoft.AspNetCore.Http.PathString("/tangent")).Should().Be(expected);
    }
}
