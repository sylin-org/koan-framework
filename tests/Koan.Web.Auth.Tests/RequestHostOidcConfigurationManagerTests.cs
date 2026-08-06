using AwesomeAssertions;
using Koan.Web.Auth.Hosting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Koan.Web.Auth.Tests;

public sealed class RequestHostOidcConfigurationManagerTests
{
    [Fact]
    public async Task Non_loopback_public_issuer_without_an_internal_binding_fails_correctively()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example.test");
        var accessor = new HttpContextAccessor { HttpContext = context };
        var manager = new RequestHostOidcConfigurationManager(
            "/.testoauth",
            accessor,
            new OpenIdConnectOptions(),
            resolveBackchannelBase: () => null);

        var action = () => manager.GetConfigurationAsync(TestContext.Current.CancellationToken);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*public issuer 'https://app.example.test/.testoauth'*No internal Kestrel address*UseUrls*");
    }
}
