using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Context;

public sealed class KoanContextHostOwnershipSpec
{
    private sealed record SharedContext(string Value);
    private sealed record HostMarker(string Name);

    private sealed class HostCarrier(HostMarker host) : IKoanContextCarrier
    {
        public string AxisKey => "test:shared";
        public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.Unverified;

        public string? Capture()
            => KoanContext.Get<SharedContext>() is { } value
                ? $"{host.Name}:{value.Value}"
                : null;

        public IDisposable Restore(string captured)
        {
            var prefix = host.Name + ":";
            if (!captured.StartsWith(prefix, StringComparison.Ordinal))
                throw KoanContextCarrierException.MalformedPayload(AxisKey);

            return KoanContext.Push(new SharedContext(captured[prefix.Length..]));
        }

        public IDisposable Suppress() => KoanContext.Suppress<SharedContext>();
    }

    [Fact]
    public void Registries_and_carrier_instances_are_owned_by_their_service_provider()
    {
        using var hostA = BuildHost("host-a");
        using var hostB = BuildHost("host-b");
        var registryA = hostA.GetRequiredService<KoanContextCarrierRegistry>();
        var registryB = hostB.GetRequiredService<KoanContextCarrierRegistry>();

        registryA.Should().NotBeSameAs(registryB);

        using (KoanContext.Push(new SharedContext("outer")))
        {
            registryA.Capture()!["test:shared"].Should().Be("host-a:outer");
            registryB.Capture()!["test:shared"].Should().Be("host-b:outer");
        }
    }

    [Fact]
    public void Same_axis_key_is_legal_once_in_each_host()
    {
        using var hostA = BuildHost("host-a");
        using var hostB = BuildHost("host-b");

        var resolveA = () => hostA.GetRequiredService<KoanContextCarrierRegistry>();
        var resolveB = () => hostB.GetRequiredService<KoanContextCarrierRegistry>();

        resolveA.Should().NotThrow();
        resolveB.Should().NotThrow();
    }

    [Fact]
    public void Explicit_outer_context_is_logical_flow_state_and_intentionally_spans_hosts()
    {
        using var hostA = BuildHost("host-a");
        using var hostB = BuildHost("host-b");

        using (KoanContext.Push(new SharedContext("flow-global")))
        {
            using (AppHost.PushScope(hostA))
            {
                AppHost.Current.Should().BeSameAs(hostA);
                KoanContext.Get<SharedContext>().Should().Be(new SharedContext("flow-global"));
            }

            using (AppHost.PushScope(hostB))
            {
                AppHost.Current.Should().BeSameAs(hostB);
                KoanContext.Get<SharedContext>().Should().Be(new SharedContext("flow-global"));
            }

            KoanContext.Get<SharedContext>().Should().Be(new SharedContext("flow-global"));
        }
    }

    [Fact]
    public void Disposing_one_host_does_not_clear_caller_owned_context_or_the_other_registry()
    {
        var hostA = BuildHost("host-a");
        using var hostB = BuildHost("host-b");
        var registryB = hostB.GetRequiredService<KoanContextCarrierRegistry>();

        using (KoanContext.Push(new SharedContext("still-owned-by-caller")))
        {
            hostA.Dispose();

            KoanContext.Get<SharedContext>().Should().Be(new SharedContext("still-owned-by-caller"));
            registryB.Capture()!["test:shared"].Should().Be("host-b:still-owned-by-caller");
        }
    }

    [Fact]
    public void Host_without_a_carrier_captures_nothing_even_when_the_flow_has_a_value()
    {
        using var composed = BuildHost("composed");
        using var bare = BuildHostWithoutCarrier();
        var composedRegistry = composed.GetRequiredService<KoanContextCarrierRegistry>();
        var bareRegistry = bare.GetRequiredService<KoanContextCarrierRegistry>();

        using (KoanContext.Push(new SharedContext("ambient")))
        {
            composedRegistry.Capture().Should().NotBeNull();
            bareRegistry.Capture().Should().BeNull();
        }
    }

    private static ServiceProvider BuildHost(string name)
    {
        var services = new ServiceCollection();
        services.AddKoanCore();
        services.AddSingleton(new HostMarker(name));
        services.AddSingleton<IKoanContextCarrier>(provider =>
            new HostCarrier(provider.GetRequiredService<HostMarker>()));
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildHostWithoutCarrier()
    {
        var services = new ServiceCollection();
        services.AddKoanCore();
        return services.BuildServiceProvider();
    }
}
