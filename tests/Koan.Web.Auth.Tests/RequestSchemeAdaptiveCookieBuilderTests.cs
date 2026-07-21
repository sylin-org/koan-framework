using System;
using AwesomeAssertions;
using Koan.Web.Auth.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Koan.Web.Auth.Tests;

/// <summary>
/// WEB-0071 regression (the critical one). A SameSite=None correlation/nonce cookie MUST be Secure or the browser
/// drops it (RFC 6265bis) → "Correlation failed" on the callback. The previous code keyed the relaxation off the
/// PROVIDER's back-channel endpoint scheme (https for a real provider), so over a plain-http front-channel the
/// cookie stayed None+non-Secure and was dropped. The builder must decide from the APP's request scheme instead:
/// relax to Lax over http, keep None+Secure over https — per request.
/// </summary>
public sealed class RequestSchemeAdaptiveCookieBuilderTests
{
    private static CookieOptions BuildFor(bool https)
    {
        // The framework default correlation cookie: None + SameAsRequest (the combination that breaks over http).
        var builder = RequestSchemeAdaptiveCookieBuilder.Wrap(new CookieBuilder
        {
            Name = ".AspNetCore.Correlation.test",
            Path = "/",
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.None,
            SecurePolicy = CookieSecurePolicy.SameAsRequest,
        });
        var ctx = new DefaultHttpContext();
        ctx.Request.IsHttps = https;
        return builder.Build(ctx, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Over_plain_http_relaxes_to_Lax_and_not_secure()
    {
        var options = BuildFor(https: false);
        options.SameSite.Should().Be(SameSiteMode.Lax,
            "a SameSite=None cookie can't be Secure over http, so the browser would drop it (\"Correlation failed\")");
        options.Secure.Should().BeFalse();
    }

    [Fact]
    public void Over_https_keeps_None_and_secure()
    {
        var options = BuildFor(https: true);
        options.SameSite.Should().Be(SameSiteMode.None, "https keeps the strict default (also supports form_post)");
        options.Secure.Should().BeTrue();
    }
}
