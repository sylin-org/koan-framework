using System.IO;
using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

public sealed class SemanticActivationCompilerSpec
{
    private const string DirectId = "Sylin.Koan.Direct";
    private const string BundleId = "Sylin.Koan.Bundle";
    private const string NestedBundleId = "Sylin.Koan.Bundle.Nested";
    private const string MemberId = "Sylin.Koan.Member";
    private const string UnrelatedId = "Sylin.Koan.Unrelated";

    [Fact]
    public void Direct_and_recursive_dependencies_are_reachable_without_activating_unrelated_descriptors()
    {
        var lifecycle = new List<string>();
        var manifest = Manifest(
            $"reference|project|Koan.Direct|{DirectId}",
            $"reference|package|{BundleId}|{BundleId}",
            $"dependency|{BundleId}|{NestedBundleId}",
            $"dependency|{NestedBundleId}|{MemberId}");
        var descriptors = new[]
        {
            Descriptor<UnrelatedMarker>(UnrelatedId, lifecycle),
            Descriptor<MemberMarker>(MemberId, lifecycle),
            Descriptor<DirectMarker>(DirectId, lifecycle),
        };

        var constitution = SemanticActivationCompiler.Compile(manifest, descriptors);

        constitution.ActiveIds.Should().BeEquivalentTo([Id(DirectId), Id(MemberId)]);
        constitution.InactiveIds.Should().Equal(Id(UnrelatedId));
        constitution.Problems.Should().BeEmpty();
        constitution.IsDegraded.Should().BeFalse();
        lifecycle.Should().BeEmpty("compilation is instance-free");

        _ = SemanticModuleRuntime.Create(constitution);

        lifecycle.Should().Contain($"factory:{DirectId}");
        lifecycle.Should().Contain($"factory:{MemberId}");
        lifecycle.Should().NotContain($"factory:{UnrelatedId}");
    }

    [Fact]
    public void Active_ids_use_deterministic_ordinal_ties_independent_of_descriptor_input_order()
    {
        const string alpha = "Sylin.Koan.alpha";
        const string middle = "Sylin.Koan.Middle";
        const string zulu = "Sylin.Koan.Zulu";
        var lifecycle = new List<string>();
        var manifest = Manifest(
            $"reference|package|{zulu}|{zulu}",
            $"reference|package|{alpha}|{alpha}",
            $"reference|package|{middle}|{middle}");
        var descriptors = new[]
        {
            Descriptor<ZuluMarker>(zulu, lifecycle),
            Descriptor<AlphaMarker>(alpha, lifecycle),
            Descriptor<MiddleMarker>(middle, lifecycle),
        };

        var first = SemanticActivationCompiler.Compile(manifest, descriptors);
        var second = SemanticActivationCompiler.Compile(manifest, descriptors.Reverse());
        var expected = new[] { Id(alpha), Id(middle), Id(zulu) }
            .OrderBy(id => id.Value, StringComparer.Ordinal);

        first.ActiveIds.Should().Equal(expected);
        second.ActiveIds.Should().Equal(first.ActiveIds);
        lifecycle.Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_ids_reject_before_any_factory_is_invoked()
    {
        const string duplicateId = "Sylin.Koan.Duplicate";
        var lifecycle = new List<string>();
        var manifest = Manifest($"reference|package|{duplicateId}|{duplicateId}");
        var descriptors = new[]
        {
            Descriptor<DuplicateOneMarker>(duplicateId, lifecycle),
            Descriptor<DuplicateTwoMarker>(duplicateId, lifecycle),
        };

        AssertStableRejectionBeforeFactory(manifest, descriptors, duplicateId, lifecycle);
    }

    [Fact]
    public void Runtime_completes_every_active_factory_before_registration_can_begin()
    {
        const string alpha = "Sylin.Koan.Barrier.Alpha";
        const string bravo = "Sylin.Koan.Barrier.Bravo";
        var lifecycle = new List<string>();
        var manifest = Manifest(
            $"reference|package|{bravo}|{bravo}",
            $"reference|package|{alpha}|{alpha}");
        var descriptors = new[]
        {
            Descriptor<BarrierBravoMarker>(bravo, lifecycle),
            Descriptor<BarrierAlphaMarker>(alpha, lifecycle),
        };
        var constitution = SemanticActivationCompiler.Compile(manifest, descriptors);

        var runtime = SemanticModuleRuntime.Create(constitution);

        lifecycle.Should().Equal($"factory:{alpha}", $"factory:{bravo}");

        runtime.Register(new ServiceCollection());

        lifecycle.Should().Equal(
            $"factory:{alpha}",
            $"factory:{bravo}",
            $"register:{alpha}",
            $"register:{bravo}");
    }

    [Fact]
    public void Factory_failure_cannot_expose_a_partially_registerable_runtime()
    {
        const string alpha = "Sylin.Koan.FactoryFailure.Alpha";
        const string bravo = "Sylin.Koan.FactoryFailure.Bravo";
        var lifecycle = new List<string>();
        var manifest = Manifest(
            $"reference|package|{alpha}|{alpha}",
            $"reference|package|{bravo}|{bravo}");
        var descriptors = new SemanticComponentDescriptor[]
        {
            Descriptor<FactorySuccessMarker>(alpha, lifecycle),
            new(
                bravo,
                typeof(ProbeModule<FactoryFailureMarker>),
                () =>
                {
                    lifecycle.Add($"factory:{bravo}");
                    throw new InvalidOperationException("planted factory failure");
                }),
        };
        var constitution = SemanticActivationCompiler.Compile(manifest, descriptors);

        var create = () => SemanticModuleRuntime.Create(constitution);

        create.Should().Throw<InvalidOperationException>().WithMessage("*planted factory failure*");
        lifecycle.Should().Equal($"factory:{alpha}", $"factory:{bravo}");
        lifecycle.Should().NotContain(entry => entry.StartsWith("register:", StringComparison.Ordinal));
    }

    [Fact]
    public void Registration_failure_is_stable_fail_closed_and_is_never_recorded_as_registered()
    {
        const string componentId = "Sylin.Koan.RegistrationFailure";
        var attempts = 0;
        var manifest = Manifest($"reference|package|{componentId}|{componentId}");
        var descriptor = new SemanticComponentDescriptor(
            componentId,
            typeof(FailingRegistrationModule),
            () => new FailingRegistrationModule(() => attempts++));
        var runtime = SemanticModuleRuntime.Create(
            SemanticActivationCompiler.Compile(manifest, [descriptor]));

        var first = Assert.Throws<SemanticModuleRuntime.SemanticRuntimeException>(() =>
            runtime.TryRegister(typeof(FailingRegistrationModule), new ServiceCollection()));
        var second = Assert.Throws<SemanticModuleRuntime.SemanticRuntimeException>(() =>
            runtime.TryRegister(typeof(FailingRegistrationModule), new ServiceCollection()));

        first.Problem.Reason.Should().Be("module-registration-failed");
        first.Problem.Should().Be(second.Problem);
        attempts.Should().Be(2, "a failed registration must never be mistaken for a completed lifecycle");
    }

    [Fact]
    public void Compiled_identity_is_bound_as_the_modules_single_identity()
    {
        const string componentId = "Sylin.Koan.AttributeOwned";
        var manifest = Manifest($"reference|package|{componentId}|{componentId}");
        var descriptor = new SemanticComponentDescriptor(
            componentId,
            typeof(AttributeOwnedModule),
            static () => new AttributeOwnedModule());

        var runtime = SemanticModuleRuntime.Create(
            SemanticActivationCompiler.Compile(manifest, [descriptor]));

        runtime.GetModule(Id(componentId)).Id.Should().Be(componentId);
    }

    [Fact]
    public void Missing_manifest_uses_an_explicitly_degraded_fallback_distinct_from_a_present_empty_manifest()
    {
        const string alpha = "Sylin.Koan.Fallback.Alpha";
        const string bravo = "Sylin.Koan.Fallback.Bravo";
        var lifecycle = new List<string>();
        var descriptors = new[]
        {
            Descriptor<FallbackBravoMarker>(bravo, lifecycle),
            Descriptor<FallbackAlphaMarker>(alpha, lifecycle),
        };
        var missingManifest = KoanApplicationReferenceManifest.Load(
            typeof(KoanApplicationReferenceManifest).Assembly);

        var degraded = SemanticActivationCompiler.Compile(missingManifest, descriptors);
        var declaredEmpty = SemanticActivationCompiler.Compile(Manifest(), descriptors);

        missingManifest.IsPresent.Should().BeFalse();
        degraded.IsDegraded.Should().BeTrue();
        degraded.ActiveIds.Should().Equal(Id(alpha), Id(bravo));
        declaredEmpty.IsDegraded.Should().BeFalse();
        declaredEmpty.ActiveIds.Should().BeEmpty();
        declaredEmpty.InactiveIds.Should().Equal(Id(alpha), Id(bravo));
    }

    [Fact]
    public void Independently_compiled_constitutions_retain_distinct_module_instances()
    {
        const string componentId = "Sylin.Koan.HostOwned";
        var firstLifecycle = new List<string>();
        var secondLifecycle = new List<string>();
        var manifest = Manifest($"reference|package|{componentId}|{componentId}");

        var firstConstitution = SemanticActivationCompiler.Compile(
            manifest,
            [Descriptor<HostOwnedMarker>(componentId, firstLifecycle)]);
        var secondConstitution = SemanticActivationCompiler.Compile(
            manifest,
            [Descriptor<HostOwnedMarker>(componentId, secondLifecycle)]);
        var firstRuntime = SemanticModuleRuntime.Create(firstConstitution);
        var secondRuntime = SemanticModuleRuntime.Create(secondConstitution);

        firstConstitution.Should().NotBeSameAs(secondConstitution);
        firstRuntime.Should().NotBeSameAs(secondRuntime);
        firstRuntime.GetModule(Id(componentId)).Should().NotBeSameAs(
            secondRuntime.GetModule(Id(componentId)));
        firstLifecycle.Should().Equal($"factory:{componentId}");
        secondLifecycle.Should().Equal($"factory:{componentId}");
    }

    private static SemanticHostConstitution AssertStableRejectionBeforeFactory(
        KoanApplicationReferenceManifest manifest,
        IReadOnlyList<SemanticComponentDescriptor> descriptors,
        string expectedOwner,
        List<string> lifecycle)
    {
        var first = SemanticActivationCompiler.Compile(manifest, descriptors);
        var second = SemanticActivationCompiler.Compile(manifest, descriptors);

        first.Problems.Should().ContainSingle();
        first.Problems.Single().Owner.Should().Be(Id(expectedOwner));
        first.Problems.Single().Reason.Should().NotBeNullOrWhiteSpace();
        first.Problems.Single().Correction.Should().NotBeNullOrWhiteSpace();
        second.Problems.Should().Equal(first.Problems, "semantic failures must be stable");

        var create = () => SemanticModuleRuntime.Create(first);
        create.Should().Throw<InvalidOperationException>();
        lifecycle.Should().BeEmpty();
        return first;
    }

    private static SemanticComponentDescriptor Descriptor<TMarker>(
        string id,
        List<string> lifecycle)
        where TMarker : class
    {
        return new SemanticComponentDescriptor(
            id,
            typeof(ProbeModule<TMarker>),
            () =>
            {
                lifecycle.Add($"factory:{id}");
                return new ProbeModule<TMarker>(id, lifecycle);
            });
    }

    private static KoanApplicationReferenceManifest Manifest(params string[] records)
    {
        using var reader = new StringReader(string.Join(
            Environment.NewLine,
            new[] { "schema|1" }.Concat(records)));
        return KoanApplicationReferenceManifest.Parse(reader);
    }

    private static SemanticId Id(string value) => new(value);

    private sealed class ProbeModule<TMarker>(string id, List<string> lifecycle) : KoanModule
        where TMarker : class
    {
        public override void Register(IServiceCollection services) => lifecycle.Add($"register:{id}");
    }

    private sealed class DirectMarker { }
    private sealed class MemberMarker { }
    private sealed class UnrelatedMarker { }
    private sealed class FoundationMarker { }
    private sealed class ConnectorMarker { }
    private sealed class DependencyComponentMarker { }
    private sealed class DependencyFoundationMarker { }
    private sealed class AlphaMarker { }
    private sealed class MiddleMarker { }
    private sealed class ZuluMarker { }
    private sealed class DuplicateOneMarker { }
    private sealed class DuplicateTwoMarker { }
    private sealed class DependentMarker { }
    private sealed class MultiProblemMarker { }
    private sealed class MultiProblemUnrelatedMarker { }
    private sealed class SelfMarker { }
    private sealed class CycleAlphaMarker { }
    private sealed class CycleBravoMarker { }
    private sealed class BarrierAlphaMarker { }
    private sealed class BarrierBravoMarker { }
    private sealed class FactorySuccessMarker { }
    private sealed class FactoryFailureMarker { }
    private sealed class FallbackAlphaMarker { }
    private sealed class FallbackBravoMarker { }
    private sealed class HostOwnedMarker { }

    private sealed class FailingRegistrationModule(Action onRegister) : KoanModule
    {
        public override void Register(IServiceCollection services)
        {
            onRegister();
            throw new InvalidOperationException("planted registration failure");
        }
    }

    internal sealed class AttributeOwnedModule : KoanModule;

}
