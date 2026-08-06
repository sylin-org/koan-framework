using Koan.AI;
using Koan.AI.Contracts;
using Koan.AI.Contracts.Sources;
using Koan.AI.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.AI.Unit.Specs.Routing;

public sealed class AiSourceRuntimeLifecycleSpec
{
    [Fact]
    public void Disable_and_remove_change_routing_visibility_immediately()
    {
        var registry = new AiSourceRegistry();
        var runtime = (IAiSourceRuntimeRegistry)registry;
        runtime.Apply(Source("local", "runtime"));

        registry.GetSourcesWithCapability("Chat").Should().ContainSingle();

        runtime.SetEnabled("local", false).Should().BeTrue();
        registry.GetSource("local")!.IsEnabled.Should().BeFalse();
        registry.GetSourcesWithCapability("Chat").Should().BeEmpty();

        runtime.SetEnabled("local", true).Should().BeTrue();
        registry.GetSourcesWithCapability("Chat").Should().ContainSingle();

        runtime.Remove("local", expectedOrigin: "another-owner").Should().BeFalse();
        registry.HasSource("local").Should().BeTrue();
        runtime.Remove("local", expectedOrigin: "runtime").Should().BeTrue();
        registry.HasSource("local").Should().BeFalse();
    }

    [Fact]
    public void A_late_health_result_cannot_mutate_a_replaced_source()
    {
        var registry = new AiSourceRegistry();
        var runtime = (IAiSourceRuntimeRegistry)registry;
        runtime.Apply(Source("local", "runtime"));
        var old = runtime.GetRuntimeSources().Single();

        runtime.Apply(Source("local", "runtime"));
        runtime.TrySetMemberHealth(
                old.Source.Name,
                old.Revision,
                old.Source.Members.Single().Name,
                MemberHealthState.Healthy)
            .Should().BeFalse();

        registry.GetSource("local")!.Members.Single().HealthState.Should().Be(MemberHealthState.Unknown);
    }

    [Fact]
    public void A_late_health_result_cannot_resurrect_a_removed_source()
    {
        var registry = new AiSourceRegistry();
        var runtime = (IAiSourceRuntimeRegistry)registry;
        runtime.Apply(Source("local", "runtime"));
        var removed = runtime.GetRuntimeSources().Single();

        runtime.Remove("local", expectedOrigin: "runtime").Should().BeTrue();

        runtime.TrySetMemberHealth(
                removed.Source.Name,
                removed.Revision,
                removed.Source.Members.Single().Name,
                MemberHealthState.Healthy)
            .Should().BeFalse();
        registry.HasSource("local").Should().BeFalse();
    }

    [Fact]
    public void AddAi_exposes_the_runtime_control_plane_without_extra_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAi();
        using var provider = services.BuildServiceProvider();

        var control = provider.GetRequiredService<IAiSourceControl>();
        control.Apply(Source("runtime", "application"));
        control.Disable("runtime").Should().BeTrue();

        provider.GetRequiredService<IAiSourceRegistry>()
            .GetSourcesWithCapability("Chat")
            .Should()
            .BeEmpty();
    }

    private static AiSourceDefinition Source(string name, string origin) => new()
    {
        Name = name,
        Provider = "ollama",
        Origin = origin,
        Members =
        [
            new AiMemberDefinition
            {
                Name = $"{name}::one",
                ConnectionString = "http://localhost:11434"
            }
        ],
        Capabilities = new Dictionary<string, AiCapabilityConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chat"] = new() { Model = "phi3" }
        }
    };
}
