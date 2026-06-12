using AwesomeAssertions;
using Koan.Web.Auth.Infrastructure;
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
    public void Allowlist_match_is_prefix_and_case_insensitive()
    {
        var r = ReturnUrlPolicy.Resolve("HTTP://MyHost.test/page?x=1", AllowList, "/");
        r.Url.Should().Be("HTTP://MyHost.test/page?x=1");
        r.IsLocal.Should().BeFalse();
    }

    [Fact]
    public void Empty_allowlist_rejects_all_absolutes()
    {
        var r = ReturnUrlPolicy.Resolve("http://myhost.test/", System.Array.Empty<string>(), "/");
        r.Url.Should().Be("/");
        r.IsLocal.Should().BeTrue();
    }
}
