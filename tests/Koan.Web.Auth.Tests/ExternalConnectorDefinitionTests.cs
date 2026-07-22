using AwesomeAssertions;
using Koan.Web.Auth.Connector.Discord.Initialization;
using Koan.Web.Auth.Connector.Google.Initialization;
using Koan.Web.Auth.Connector.Microsoft.Initialization;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Web.Auth.Tests;

public sealed class ExternalConnectorDefinitionTests
{
    [Fact]
    public void References_are_inert_until_credentials_are_configured()
    {
        var plan = Compile(new AuthOptions());

        plan.Default.Should().BeNull();
        plan.Providers.Should().HaveCount(3).And.OnlyContain(provider =>
            provider.State == "inactive" &&
            !provider.Eligible &&
            provider.Reason == "configuration-required");
    }

    [Fact]
    public void Configured_connectors_compile_exact_provider_protocols_and_election()
    {
        var options = new AuthOptions
        {
            Providers = new Dictionary<string, ProviderOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["google"] = Credentials(),
                ["microsoft"] = Credentials(),
                ["discord"] = Credentials()
            }
        };

        var plan = Compile(options);

        plan.Default!.Id.Should().Be("google");
        plan.Providers.Should().HaveCount(3).And.OnlyContain(provider =>
            provider.Eligible && provider.State == "eligible" && provider.Explicit);

        var google = plan.FindRoute("google")!;
        google.Info.Protocol.Should().Be(AuthProviderProtocols.Oidc);
        google.Info.ChallengePath.Should().Be("/auth/google/challenge");
        google.Options.Authority.Should().Be("https://accounts.google.com");
        google.Options.Scopes.Should().Equal("openid", "email", "profile");

        var microsoft = plan.FindRoute("microsoft")!;
        microsoft.Info.Protocol.Should().Be(AuthProviderProtocols.Oidc);
        microsoft.Info.ChallengePath.Should().Be("/auth/microsoft/challenge");
        microsoft.Options.Authority.Should().Be("https://login.microsoftonline.com/common/v2.0");
        microsoft.Options.Scopes.Should().Equal("openid", "email", "profile");

        var discord = plan.FindRoute("discord")!;
        discord.Info.Protocol.Should().Be(AuthProviderProtocols.OAuth2);
        discord.Info.ChallengePath.Should().Be("/auth/discord/challenge");
        discord.Options.AuthorizationEndpoint.Should().Be("https://discord.com/api/oauth2/authorize");
        discord.Options.TokenEndpoint.Should().Be("https://discord.com/api/oauth2/token");
        discord.Options.UserInfoEndpoint.Should().Be("https://discord.com/api/users/@me");
        discord.Options.Scopes.Should().Equal("identify", "email");
    }

    private static ProviderOptions Credentials() => new()
    {
        ClientId = "client",
        ClientSecret = "secret"
    };

    private static AuthProviderPlan Compile(AuthOptions options)
    {
        var services = new ServiceCollection();
        new GoogleAuthModule().Register(services);
        new MicrosoftAuthModule().Register(services);
        new DiscordAuthModule().Register(services);
        using var provider = services.BuildServiceProvider();
        return new AuthProviderPlan(
            Microsoft.Extensions.Options.Options.Create(options),
            provider.GetServices<AuthProviderDefinition>());
    }
}
