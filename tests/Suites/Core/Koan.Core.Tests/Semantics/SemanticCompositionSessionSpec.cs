using System.IO;
using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

public sealed class SemanticCompositionSessionSpec
{
    private const string ComponentId = "Sylin.Koan.Session.Probe";

    [Fact]
    public void GetOrCreate_is_stable_per_service_collection_and_isolated_between_collections()
    {
        var firstServices = new ServiceCollection();
        var secondServices = new ServiceCollection();

        var first = SemanticCompositionSession.GetOrCreate(firstServices);
        var repeated = SemanticCompositionSession.GetOrCreate(firstServices);
        var second = SemanticCompositionSession.GetOrCreate(secondServices);

        repeated.Should().BeSameAs(first);
        second.Should().NotBeSameAs(first);
        firstServices
            .Where(descriptor => descriptor.ServiceType == typeof(SemanticCompositionSession))
            .Should().ContainSingle();
        secondServices
            .Where(descriptor => descriptor.ServiceType == typeof(SemanticCompositionSession))
            .Should().ContainSingle();
    }

    [Fact]
    public void Completing_the_same_descriptor_and_manifest_for_two_collections_keeps_host_state_and_modules_distinct()
    {
        var manifest = Manifest();
        var constructed = new List<ProbeModule>();
        var descriptor = new SemanticComponentDescriptor(
            ComponentId,
            typeof(ProbeModule),
            () =>
            {
                var module = new ProbeModule();
                constructed.Add(module);
                return module;
            });
        var descriptors = new[] { descriptor };
        var firstServices = new ServiceCollection();
        var secondServices = new ServiceCollection();
        var firstSession = SemanticCompositionSession.GetOrCreate(firstServices);
        var secondSession = SemanticCompositionSession.GetOrCreate(secondServices);
        var firstConstitution = SemanticActivationCompiler.Compile(manifest, descriptors);
        var secondConstitution = SemanticActivationCompiler.Compile(manifest, descriptors);
        var firstRuntime = SemanticModuleRuntime.Create(firstConstitution);
        var secondRuntime = SemanticModuleRuntime.Create(secondConstitution);

        firstSession.CompleteModuleInitialization(manifest, firstConstitution, firstRuntime);
        secondSession.CompleteModuleInitialization(manifest, secondConstitution, secondRuntime);

        using var firstProvider = firstServices.BuildServiceProvider();
        using var secondProvider = secondServices.BuildServiceProvider();
        var firstResolvedSession = firstProvider.GetRequiredService<SemanticCompositionSession>();
        var secondResolvedSession = secondProvider.GetRequiredService<SemanticCompositionSession>();
        var firstResolvedConstitution = firstProvider.GetRequiredService<SemanticHostConstitution>();
        var secondResolvedConstitution = secondProvider.GetRequiredService<SemanticHostConstitution>();
        var firstResolvedRuntime = firstProvider.GetRequiredService<SemanticModuleRuntime>();
        var secondResolvedRuntime = secondProvider.GetRequiredService<SemanticModuleRuntime>();

        firstResolvedSession.Should().BeSameAs(firstSession);
        secondResolvedSession.Should().BeSameAs(secondSession);
        firstResolvedSession.Should().NotBeSameAs(secondResolvedSession);
        firstResolvedConstitution.Should().BeSameAs(firstConstitution);
        secondResolvedConstitution.Should().BeSameAs(secondConstitution);
        firstResolvedConstitution.Should().NotBeSameAs(secondResolvedConstitution);
        firstResolvedRuntime.Should().BeSameAs(firstRuntime);
        secondResolvedRuntime.Should().BeSameAs(secondRuntime);
        firstResolvedRuntime.Should().NotBeSameAs(secondResolvedRuntime);
        firstResolvedRuntime.GetModule(new SemanticId(ComponentId)).Should().NotBeSameAs(
            secondResolvedRuntime.GetModule(new SemanticId(ComponentId)));
        constructed.Should().HaveCount(2);
    }

    [Fact]
    public void Nested_leases_freeze_only_after_the_successful_outer_lease_completes()
    {
        var session = SemanticCompositionSession.GetOrCreate(new ServiceCollection());
        var outer = session.Enter();
        var inner = session.Enter();

        inner.Complete();
        inner.Dispose();

        session.IsFrozen.Should().BeFalse();

        outer.Complete();
        outer.Dispose();

        session.IsFrozen.Should().BeTrue();
        var reenter = () => session.Enter();
        reenter.Should().Throw<InvalidOperationException>().WithMessage("*already frozen*");
    }

    [Fact]
    public void Abandoned_internal_lease_does_not_freeze_the_session()
    {
        var session = SemanticCompositionSession.GetOrCreate(new ServiceCollection());

        var compose = () =>
        {
            using var lease = session.Enter();
            throw new InvalidOperationException("planted composition failure");
        };

        compose.Should().Throw<InvalidOperationException>().WithMessage("*planted composition failure*");
        session.IsFrozen.Should().BeFalse();

        using (var retry = session.Enter())
        {
            retry.Complete();
        }

        session.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void Repeated_parameterless_AddKoan_after_freeze_reuses_the_compiled_runtime_without_reconstruction()
    {
        var services = new ServiceCollection();
        var session = SemanticCompositionSession.GetOrCreate(services);
        var manifest = Manifest();
        var factoryCalls = 0;
        var descriptor = new SemanticComponentDescriptor(
            ComponentId,
            typeof(ProbeModule),
            () =>
            {
                factoryCalls++;
                return new ProbeModule();
            });
        var constitution = SemanticActivationCompiler.Compile(manifest, [descriptor]);
        var runtime = SemanticModuleRuntime.Create(constitution);
        session.CompleteModuleInitialization(manifest, constitution, runtime);
        using (var lease = session.Enter())
        {
            lease.Complete();
        }

        services.AddKoan();
        services.AddKoan();

        factoryCalls.Should().Be(1);
        SemanticCompositionSession.GetOrCreate(services).Should().BeSameAs(session);
        services.Where(service => service.ServiceType == typeof(SemanticModuleRuntime))
            .Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeSameAs(runtime);
    }

    [Fact]
    public void Failed_module_initialization_faults_the_collection_instead_of_allowing_a_partial_retry()
    {
        var session = SemanticCompositionSession.GetOrCreate(new ServiceCollection());
        session.TryBeginModuleInitialization().Should().BeTrue();
        session.FailModuleInitialization(new InvalidOperationException("planted boot failure"));

        var retry = () => session.TryBeginModuleInitialization();

        retry.Should().Throw<InvalidOperationException>()
            .WithMessage("*previous Koan module initialization failed*new service collection*planted boot failure*");
    }

    [Fact]
    public void Faulted_composition_cannot_be_completed_or_reentered_after_partial_application_changes()
    {
        var session = SemanticCompositionSession.GetOrCreate(new ServiceCollection());
        using var lease = session.Enter();
        session.FailComposition(new InvalidOperationException("planted declaration failure"));

        var complete = () => lease.Complete();
        var reenter = () => session.Enter();

        complete.Should().Throw<InvalidOperationException>()
            .WithMessage("*Koan application composition failed*new service collection*planted declaration failure*");
        reenter.Should().Throw<InvalidOperationException>()
            .WithMessage("*Koan application composition failed*new service collection*planted declaration failure*");
        session.IsFrozen.Should().BeFalse();
    }

    private static KoanApplicationReferenceManifest Manifest()
    {
        using var reader = new StringReader($"schema|1{Environment.NewLine}reference|package|{ComponentId}|{ComponentId}");
        return KoanApplicationReferenceManifest.Parse(reader);
    }

    private sealed class ProbeModule : KoanModule;
}
