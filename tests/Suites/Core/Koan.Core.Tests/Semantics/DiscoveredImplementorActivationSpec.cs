using System.IO;
using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Hosting.Registry;
using Koan.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

/// <summary>
/// ARCH-0138: a discovered implementor contributes to a host only when its assembly is not one whose Koan modules the
/// host's constitution left inactive.
/// </summary>
public sealed class DiscoveredImplementorActivationSpec
{
    public DiscoveredImplementorActivationSpec()
    {
        KoanRegistry.RegisterDiscoveredImplementors(typeof(IProbeContribution), [typeof(ProbeContribution)]);
    }

    [Fact]
    public void An_implementor_in_an_assembly_with_only_inactive_modules_is_excluded()
    {
        var services = Compose(active: [], inactive: [Descriptor<InactiveProbeModule>("Sylin.Koan.Spec.Discovered.Inactive")], degraded: false);

        KoanRegistry.GetDiscoveredImplementors(typeof(IProbeContribution), services).Should().BeEmpty();
        KoanRegistry.GetDiscoveredImplementors(typeof(IProbeContribution)).Should().Contain(typeof(ProbeContribution));
    }

    [Fact]
    public void A_degraded_constitution_keeps_every_discovered_implementor()
    {
        var services = Compose(active: [], inactive: [Descriptor<InactiveProbeModule>("Sylin.Koan.Spec.Discovered.Inactive")], degraded: true);

        KoanRegistry.GetDiscoveredImplementors(typeof(IProbeContribution), services).Should().Contain(typeof(ProbeContribution));
    }

    [Fact]
    public void An_implementor_in_an_assembly_with_an_active_module_is_kept()
    {
        var services = Compose(
            active: [Descriptor<ActiveProbeModule>("Sylin.Koan.Spec.Discovered.Active")],
            inactive: [Descriptor<InactiveProbeModule>("Sylin.Koan.Spec.Discovered.Inactive")],
            degraded: false);

        KoanRegistry.GetDiscoveredImplementors(typeof(IProbeContribution), services).Should().Contain(typeof(ProbeContribution));
    }

    [Fact]
    public void A_collection_without_a_constitution_keeps_every_discovered_implementor()
    {
        KoanRegistry.GetDiscoveredImplementors(typeof(IProbeContribution), new ServiceCollection())
            .Should().Contain(typeof(ProbeContribution));
    }

    private static ServiceCollection Compose(
        SemanticComponentDescriptor[] active,
        SemanticComponentDescriptor[] inactive,
        bool degraded)
    {
        var services = new ServiceCollection();
        var constitution = new SemanticHostConstitution(active, inactive, [], [], degraded);
        using var reader = new StringReader("schema|1");
        SemanticCompositionSession.GetOrCreate(services).CompleteModuleInitialization(
            KoanApplicationReferenceManifest.Parse(reader),
            constitution,
            SemanticModuleRuntime.Create(constitution));
        return services;
    }

    private static SemanticComponentDescriptor Descriptor<TModule>(string id)
        where TModule : KoanModule, new()
        => new(id, typeof(TModule), static () => new TModule());

    public interface IProbeContribution;

    public sealed class ProbeContribution : IProbeContribution;

    public sealed class InactiveProbeModule : KoanModule;

    public sealed class ActiveProbeModule : KoanModule;
}
