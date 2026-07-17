using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Diagnostics;
using Koan.Core.Semantics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

public sealed class SemanticModuleEvidenceSpec
{
    [Fact]
    public void Retained_instance_owns_registration_and_evidence()
    {
        EvidenceModule? created = null;
        var runtime = Runtime(Descriptor(
            "Sylin.Koan.Spec.Evidence",
            () => created = new EvidenceModule("retained")));
        var services = new ServiceCollection();

        runtime.Register(services);
        using var provider = services.BuildServiceProvider();
        var builder = new KoanCompositionBuilder();
        runtime.ReportComposition(builder, provider);

        created.Should().NotBeNull();
        runtime.GetModule(new SemanticId("Sylin.Koan.Spec.Evidence")).Should().BeSameAs(created);
        created!.RegisterCalls.Should().Be(1);
        created.EvidenceCalls.Should().Be(1);
        Facts(builder).Should().ContainSingle(fact =>
            fact.Code == EvidenceModule.Code
            && fact.Subject == "retained");
    }

    [Fact]
    public void Inactive_descriptor_is_not_constructed_or_reported()
    {
        var inactiveFactories = 0;
        var active = Descriptor(
            "Sylin.Koan.Spec.Active",
            () => new EvidenceModule("active"));
        var inactive = Descriptor(
            "Sylin.Koan.Spec.Inactive",
            () =>
            {
                inactiveFactories++;
                return new EvidenceModule("inactive");
            });
        var constitution = new SemanticHostConstitution(
            [active],
            [inactive],
            [],
            [],
            isDegraded: false);
        var runtime = SemanticModuleRuntime.Create(constitution);
        using var provider = new ServiceCollection().BuildServiceProvider();
        var builder = new KoanCompositionBuilder();

        runtime.ReportComposition(builder, provider);

        inactiveFactories.Should().Be(0);
        Facts(builder).Should().ContainSingle(fact => fact.Subject == "active");
        Facts(builder).Should().NotContain(fact => fact.Subject == "inactive");
    }

    [Fact]
    public void Independent_hosts_retain_distinct_module_and_evidence_state()
    {
        var sequence = 0;
        var descriptor = Descriptor(
            "Sylin.Koan.Spec.MultiHost",
            () => new EvidenceModule($"host-{++sequence}"));
        var first = Runtime(descriptor);
        var second = Runtime(descriptor);
        using var firstProvider = new ServiceCollection().BuildServiceProvider();
        using var secondProvider = new ServiceCollection().BuildServiceProvider();
        var firstBuilder = new KoanCompositionBuilder();
        var secondBuilder = new KoanCompositionBuilder();

        first.ReportComposition(firstBuilder, firstProvider);
        second.ReportComposition(secondBuilder, secondProvider);

        first.GetModule(new SemanticId("Sylin.Koan.Spec.MultiHost"))
            .Should().NotBeSameAs(second.GetModule(new SemanticId("Sylin.Koan.Spec.MultiHost")));
        Facts(firstBuilder).Single(fact => fact.Code == EvidenceModule.Code).Subject.Should().Be("host-1");
        Facts(secondBuilder).Single(fact => fact.Code == EvidenceModule.Code).Subject.Should().Be("host-2");
    }

    [Fact]
    public void Throwing_module_isolated_as_safe_collection_failure()
    {
        var runtime = Runtime(
            Descriptor("Sylin.Koan.Spec.Good", () => new EvidenceModule("good")),
            Descriptor("Sylin.Koan.Spec.Bad", static () => new ThrowingEvidenceModule()));
        using var provider = new ServiceCollection().BuildServiceProvider();
        var builder = new KoanCompositionBuilder();

        runtime.ReportComposition(builder, provider);

        var facts = Facts(builder);
        facts.Should().ContainSingle(fact => fact.Code == EvidenceModule.Code && fact.Subject == "good");
        facts.Should().ContainSingle(fact =>
            fact.Code == Koan.Core.Infrastructure.Constants.Diagnostics.Codes.CollectionFailed
            && fact.State == KoanFactState.CollectionFailed
            && fact.Subject == "Sylin.Koan.Spec.Bad"
            && fact.Correction != null);
    }

    private static SemanticModuleRuntime Runtime(params SemanticComponentDescriptor[] active) =>
        SemanticModuleRuntime.Create(new SemanticHostConstitution(
            active,
            [],
            [],
            [],
            isDegraded: false));

    private static SemanticComponentDescriptor Descriptor<TModule>(
        string id,
        Func<TModule> factory)
        where TModule : KoanModule =>
        new(id, typeof(TModule), () => factory());

    private static IReadOnlyList<KoanFact> Facts(KoanCompositionBuilder builder)
    {
        builder.ApplyTo(out _, out _, out _, out _, out var facts);
        return facts;
    }

    private sealed class EvidenceModule(string subject) : KoanModule
    {
        public const string Code = "koan.spec.module-evidence";

        public int RegisterCalls { get; private set; }

        public int EvidenceCalls { get; private set; }

        public override void Register(IServiceCollection services) => RegisterCalls++;

        public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
        {
            EvidenceCalls++;
            composition.AddObservation(Code, subject, "Retained evidence.", "retained-instance", Id);
        }
    }

    private sealed class ThrowingEvidenceModule : KoanModule
    {
        public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services) =>
            throw new InvalidOperationException("private failure detail");
    }
}
