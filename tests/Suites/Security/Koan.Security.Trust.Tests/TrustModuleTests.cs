using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Security.Trust.Inbound;
using Koan.Security.Trust.Issuer;
using Xunit;

namespace Koan.Security.Trust.Tests;

public sealed class TrustModuleTests
{
    [Fact]
    public async Task Module_registers_one_ES256_issuer_and_its_bearer_scheme()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new TrustModule().Register(services);

        await using var provider = services.BuildServiceProvider();
        var issuer = provider.GetRequiredService<IIssuer>();
        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(KoanBearerDefaults.AuthenticationScheme);

        issuer.PublishedKeys.Should().ContainSingle().Which.Alg.Should().Be("ES256");
        provider.GetServices<IIssuer>().Should().ContainSingle();
        scheme.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Koan:Security:Trust:Issuer", "")]
    [InlineData("Koan:Security:Trust:Audience", "")]
    [InlineData("Koan:Security:Trust:DefaultLifetimeMinutes", "0")]
    public void Invalid_issuer_configuration_fails_options_validation(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        new TrustModule().Register(services);

        using var provider = services.BuildServiceProvider();
        Action resolve = () => _ = provider.GetRequiredService<IOptions<TrustIssuerOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>();
    }
}
