using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Options;
using Koan.Web.Auth.Providers;
using Xunit;

namespace Koan.Web.Auth.Tests;

public sealed class AuthProviderPlanTests
{
    [Fact]
    public void Unconfigured_connector_is_inactive_and_automatic_local_provider_wins()
    {
        var plan = Compile(
            new AuthOptions(),
            Oidc("google"),
            LocalOidc());

        plan.Find("google")!.State.Should().Be("inactive");
        plan.Find("google")!.Eligible.Should().BeFalse();
        plan.Default!.Id.Should().Be("test-oidc");
        plan.Default.Reason.Should().Be("automatic-local-provider");
    }

    [Fact]
    public void Explicit_complete_provider_wins_over_automatic_default()
    {
        var plan = Compile(
            new AuthOptions
            {
                Providers = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["google"] = new ProviderOptions
                    {
                        ClientId = "client",
                        ClientSecret = "secret"
                    }
                }
            },
            Oidc("google"),
            LocalOidc());

        plan.Default!.Id.Should().Be("google");
        plan.Default.Explicit.Should().BeTrue();
    }

    [Fact]
    public void Explicit_incomplete_provider_fails_with_exact_correction()
    {
        var act = () => Compile(
            new AuthOptions
            {
                Providers = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["google"] = new ProviderOptions { ClientId = "client" }
                }
            },
            Oidc("google"));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*google*ClientSecret*Koan:Web:Auth:Providers:google*");
    }

    [Fact]
    public void Preferred_inactive_provider_fails_instead_of_silently_falling_back()
    {
        var act = () => Compile(
            new AuthOptions { PreferredProviderId = "google" },
            Oidc("google"),
            LocalOidc());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PreferredProviderId 'google'*not eligible*Configure Koan:Web:Auth:Providers:google*");
    }

    [Fact]
    public void Config_only_oidc_provider_is_a_first_class_candidate()
    {
        var plan = Compile(new AuthOptions
        {
            Providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["corporate"] = new ProviderOptions
                {
                    Type = AuthProviderProtocols.Oidc,
                    Authority = "https://identity.example.test",
                    ClientId = "client",
                    ClientSecret = "secret"
                }
            }
        });

        plan.Default!.Id.Should().Be("corporate");
        plan.Default.Explicit.Should().BeTrue();
        plan.Find("corporate")!.Protocol.Should().Be(AuthProviderProtocols.Oidc);
    }

    [Fact]
    public void Plans_are_host_owned_and_do_not_share_state()
    {
        var first = Compile(new AuthOptions(), LocalOidc());
        var second = Compile(new AuthOptions(), Oidc("google"));

        first.Default!.Id.Should().Be("test-oidc");
        second.Default.Should().BeNull();
        second.Find("test-oidc").Should().BeNull();
    }

    private static AuthProviderPlan Compile(AuthOptions options, params AuthProviderDefinition[] definitions)
        => new(Microsoft.Extensions.Options.Options.Create(options), definitions);

    private static AuthProviderDefinition Oidc(string id)
        => AuthProviderDefinition.Oidc(
            id,
            id,
            $"/icons/{id}.svg",
            $"https://{id}.example.test",
            ["openid", "profile"],
            priority: 200);

    private static AuthProviderDefinition LocalOidc()
        => new(
            "test-oidc",
            new ProviderOptions
            {
                Type = AuthProviderProtocols.Oidc,
                DisplayName = "Test OIDC",
                Authority = "/.testoauth",
                ClientId = "test-client",
                ClientSecret = "test-secret",
                Scopes = ["openid"],
                Priority = 1
            },
            Automatic: true);
}
