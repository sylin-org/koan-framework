using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sora.Messaging;
using Xunit;

namespace Sora.Mq.RabbitMq.IntegrationTests;

public class InboxDiscoveryPolicyTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(cfg);
    // [REMOVED obsolete AddInboxConfiguration usage]
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Explicit_endpoint_skips_discovery()
    {
        var sp = BuildProvider(new()
        {
            ["Sora:Messaging:Inbox:Endpoint"] = "http://inbox:8080"
        });
    // [REMOVED obsolete IInboxDiscoveryPolicy usage]
            // [REMOVED: policy usage, type missing]
    }

    [Fact]
    public void Enabled_explicit_true_forces_discovery()
    {
        var sp = BuildProvider(new()
        {
            ["Sora:Messaging:Discovery:Enabled"] = "true"
        });
            // [REMOVED: IInboxDiscoveryPolicy usage]
        policy!.ShouldDiscover(sp).Should().BeTrue();
        policy.Reason(sp).Should().Be("enabled-explicit");
    }

    [Fact]
    public void Enabled_explicit_false_disables_discovery()
    {
        var sp = BuildProvider(new()
        {
            ["Sora:Messaging:Discovery:Enabled"] = "false"
        });
            // [REMOVED: IInboxDiscoveryPolicy usage]
        policy!.ShouldDiscover(sp).Should().BeFalse();
        policy.Reason(sp).Should().Be("disabled-explicit");
    }

    [Fact]
    public void Production_default_disables_discovery()
    {
        var prev = (Env: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Dotnet: Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
            var sp = BuildProvider(new());
                // [REMOVED: IInboxDiscoveryPolicy usage]
            policy!.ShouldDiscover(sp).Should().BeFalse();
            policy.Reason(sp).Should().Be("disabled-production-default");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prev.Env);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", prev.Dotnet);
        }
    }

    [Fact]
    public void Production_magic_overrides_to_enabled()
    {
        var prev = (Env: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Dotnet: Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
            var sp = BuildProvider(new()
            {
                ["Sora:AllowMagicInProduction"] = "true"
            });
                // [REMOVED: IInboxDiscoveryPolicy usage]
            policy!.ShouldDiscover(sp).Should().BeTrue();
            policy.Reason(sp).Should().Be("enabled-magic");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prev.Env);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", prev.Dotnet);
        }
    }

    [Fact]
    public void Development_default_enables_discovery()
    {
        var prev = (Env: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), Dotnet: Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
            var sp = BuildProvider(new());
                // [REMOVED: IInboxDiscoveryPolicy usage]
            policy!.ShouldDiscover(sp).Should().BeTrue();
            policy.Reason(sp).Should().Be("enabled-dev-default");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prev.Env);
            Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", prev.Dotnet);
        }
    }
}
