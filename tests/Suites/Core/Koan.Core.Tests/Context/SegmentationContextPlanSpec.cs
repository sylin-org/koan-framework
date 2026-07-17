using AwesomeAssertions;
using Koan.Core.Context;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;
using Xunit;

namespace Koan.Core.Tests.Context;

public sealed class SegmentationContextPlanSpec
{
    private sealed record AxisContext(string Value);
    private sealed class OrdinarySubject;
    private sealed class HostSubject;

    [Fact]
    public void Capture_binds_the_subject_and_returns_its_declared_hard_axis()
    {
        var carrier = new AxisCarrier();
        var plan = Plan(carrier);

        using var ambient = KoanContext.Push(new AxisContext("private-value"));
        var captured = plan.Capture(typeof(OrdinarySubject), "spec capture");

        captured.Should().ContainSingle("spec:axis", "v1:private-value");
        plan.MinimumIngressTrust.Should().Be(ContextIngressTrust.Authenticated);
    }

    [Fact]
    public void Missing_carrier_mapping_rejects_before_async_work()
    {
        using var ambient = KoanContext.Push(new AxisContext("private-value"));
        var plan = Plan();

        var capture = () => plan.Capture(typeof(OrdinarySubject), "spec capture");

        var failure = capture.Should().Throw<SegmentationContextException>().Which;
        failure.Failure.Should().Be(SegmentationContextException.FailureKind.MissingCarrier);
        failure.DimensionId.Should().Be("tenant");
        failure.Message.Should().NotContain("private-value");
    }

    [Fact]
    public void Carrier_that_omits_a_bound_dimension_rejects_without_exposing_its_value()
    {
        using var ambient = KoanContext.Push(new AxisContext("private-value"));
        var plan = Plan(new AxisCarrier(captureValue: false));

        var capture = () => plan.Capture(typeof(OrdinarySubject), "spec capture");

        var failure = capture.Should().Throw<SegmentationContextException>().Which;
        failure.Failure.Should().Be(SegmentationContextException.FailureKind.MissingCapturedAxis);
        failure.Message.Should().NotContain("private-value");
    }

    [Fact]
    public void Required_capture_can_materialize_a_deterministically_resolved_fallback()
    {
        var plan = Plan(
            [new AxisCarrier(captureValue: false, materializeValue: true)],
            fallbackValue: "resolved-dev");

        var captured = plan.Capture(typeof(OrdinarySubject), "spec capture");

        captured.Should().ContainSingle("spec:axis", "v1:resolved-dev");
        KoanContext.Get<AxisContext>().Should().BeNull("materialization must not mutate the submitting flow");
    }

    [Fact]
    public void Restore_requires_the_subjects_axis_before_any_carrier_scope_is_touched()
    {
        var carrier = new AxisCarrier();
        var plan = Plan(carrier);

        var restore = () => plan.Restore(
            typeof(OrdinarySubject),
            captured: null,
            ContextIngressTrust.HostTrusted,
            "spec restore");

        restore.Should().Throw<SegmentationContextException>()
            .Which.Failure.Should().Be(SegmentationContextException.FailureKind.MissingCapturedAxis);
        carrier.RestoreCalls.Should().Be(0);
        carrier.SuppressCalls.Should().Be(0);
    }

    [Fact]
    public void Restore_enforces_trust_then_rebinds_the_dimension_inside_the_returned_scope()
    {
        var carrier = new AxisCarrier();
        var plan = Plan(carrier);
        var captured = new Dictionary<string, string> { ["spec:axis"] = "v1:restored" };

        var untrusted = () => plan.Restore(
            typeof(OrdinarySubject),
            captured,
            ContextIngressTrust.Unverified,
            "spec restore");
        untrusted.Should().Throw<KoanContextCarrierException>()
            .Which.Failure.Should().Be(KoanContextCarrierException.FailureKind.InsufficientIngressTrust);

        using (plan.Restore(
                   typeof(OrdinarySubject),
                   captured,
                   ContextIngressTrust.Authenticated,
                   "spec restore"))
            KoanContext.Get<AxisContext>().Should().Be(new AxisContext("restored"));

        KoanContext.Get<AxisContext>().Should().BeNull();
    }

    [Fact]
    public void A_subject_outside_the_dimension_has_the_empty_floor_without_a_carrier()
    {
        var plan = Plan();

        plan.Capture(typeof(HostSubject), "host capture").Should().BeNull();
        using (plan.Restore(
                   typeof(HostSubject),
                   captured: null,
                   ContextIngressTrust.Unverified,
                   "host restore"))
            KoanContext.Get<AxisContext>().Should().BeNull();
    }

    [Fact]
    public void Two_carriers_cannot_claim_the_same_hard_dimension()
    {
        var registry = () => new KoanContextCarrierRegistry([
            new AxisCarrier("spec:first"),
            new AxisCarrier("spec:second")
        ]);

        registry.Should().Throw<InvalidOperationException>()
            .WithMessage("*represented by more than one context carrier*");
    }

    private static SegmentationContextPlan Plan(params IKoanContextCarrier[] carriers)
        => Plan(carriers, fallbackValue: null);

    private static SegmentationContextPlan Plan(
        IKoanContextCarrier[] carriers,
        string? fallbackValue)
    {
        var builder = new SegmentationPlanBuilder();
        builder.ForOwner(new SemanticId("spec.owner")).Require(
            "tenant",
            () => KoanContext.Get<AxisContext>() is { } current
                ? SegmentationValue.For(current.Value)
                : fallbackValue is { Length: > 0 }
                    ? SegmentationValue.For(fallbackValue)
                    : SegmentationValue.Missing,
            static type => type != typeof(HostSubject),
            "Establish the required context.");
        return new SegmentationContextPlan(
            builder.Build(),
            new KoanContextCarrierRegistry(carriers));
    }

    private sealed class AxisCarrier(
        string axisKey = "spec:axis",
        bool captureValue = true,
        bool materializeValue = false) : IKoanContextCarrier
    {
        public string AxisKey => axisKey;
        public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.Authenticated;
        public IReadOnlyCollection<string> SegmentationDimensions => ["tenant"];
        public int RestoreCalls { get; private set; }
        public int SuppressCalls { get; private set; }

        public string? Capture()
            => captureValue && KoanContext.Get<AxisContext>() is { } current
                ? "v1:" + current.Value
                : null;

        public string? CaptureRequired(string dimensionId, string value)
            => materializeValue ? "v1:" + value : Capture();

        public IDisposable Restore(string captured)
        {
            RestoreCalls++;
            return KoanContext.Push(new AxisContext(captured["v1:".Length..]));
        }

        public IDisposable Suppress()
        {
            SuppressCalls++;
            return KoanContext.Suppress<AxisContext>();
        }
    }
}
