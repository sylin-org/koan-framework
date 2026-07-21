using AwesomeAssertions;
using Koan.Core.Context;
using Xunit;

namespace Koan.Core.Tests.Context;

public sealed class KoanContextCarrierRegistrySpec
{
    private sealed record FirstContext(string Value);
    private sealed record SecondContext(string Value);

    private sealed class TestCarrier<TContext> : IKoanContextCarrier where TContext : class
    {
        private readonly Func<TContext, string> _capture;
        private readonly Func<string, TContext> _restore;
        private readonly Func<Exception?>? _captureFailure;
        private readonly Func<string, Exception?>? _restoreFailure;
        private readonly IList<string>? _trace;

        public TestCarrier(
            string axisKey,
            ContextIngressTrust minimumIngressTrust,
            Func<TContext, string> capture,
            Func<string, TContext> restore,
            IList<string>? trace = null,
            Func<Exception?>? captureFailure = null,
            Func<string, Exception?>? restoreFailure = null)
        {
            AxisKey = axisKey;
            MinimumIngressTrust = minimumIngressTrust;
            _capture = capture;
            _restore = restore;
            _trace = trace;
            _captureFailure = captureFailure;
            _restoreFailure = restoreFailure;
        }

        public string AxisKey { get; }
        public ContextIngressTrust MinimumIngressTrust { get; }
        public int RestoreCalls { get; private set; }
        public int SuppressCalls { get; private set; }

        public string? Capture()
        {
            if (_captureFailure?.Invoke() is { } failure)
                throw failure;

            return KoanContext.Get<TContext>() is { } value ? _capture(value) : null;
        }

        public IDisposable Restore(string captured)
        {
            RestoreCalls++;
            _trace?.Add($"restore:{AxisKey}");
            if (_restoreFailure?.Invoke(captured) is { } failure)
                throw failure;

            return Track(KoanContext.Push(_restore(captured)), "dispose");
        }

        public IDisposable Suppress()
        {
            SuppressCalls++;
            _trace?.Add($"suppress:{AxisKey}");
            return Track(KoanContext.Suppress<TContext>(), "dispose-suppress");
        }

        private IDisposable Track(IDisposable inner, string action)
            => _trace is null ? inner : new TrackingScope(inner, _trace, $"{action}:{AxisKey}");
    }

    private sealed class TrackingScope(IDisposable inner, IList<string> trace, string disposeEntry) : IDisposable
    {
        private IDisposable? _inner = inner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _inner, null);
            if (current is null) return;
            current.Dispose();
            trace.Add(disposeEntry);
        }
    }

    private sealed class CallbackScope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private sealed class DisposalFailureCarrier(string axisKey, IList<string> trace, string secret)
        : IKoanContextCarrier
    {
        public string AxisKey => axisKey;
        public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.Unverified;
        public string? Capture() => null;
        public IDisposable Restore(string captured)
            => new CallbackScope(() =>
            {
                trace.Add($"dispose:{axisKey}");
                throw new InvalidOperationException(secret);
            });
        public IDisposable Suppress() => Restore(string.Empty);
    }

    private sealed class MutableDeclarationCarrier : IKoanContextCarrier
    {
        public string AxisKey { get; set; } = "test:stable";
        public ContextIngressTrust MinimumIngressTrust { get; set; } = ContextIngressTrust.Authenticated;
        public string? Capture() => "captured";
        public IDisposable Restore(string captured) => KoanContext.Push(new FirstContext(captured));
        public IDisposable Suppress() => KoanContext.Suppress<FirstContext>();
    }

    private static TestCarrier<FirstContext> First(
        string key = "test:first",
        ContextIngressTrust trust = ContextIngressTrust.Unverified,
        IList<string>? trace = null,
        Func<Exception?>? captureFailure = null,
        Func<string, Exception?>? restoreFailure = null)
        => new(
            key,
            trust,
            static value => value.Value,
            static captured => new FirstContext(captured),
            trace,
            captureFailure,
            restoreFailure);

    private static TestCarrier<SecondContext> Second(
        string key = "test:second",
        ContextIngressTrust trust = ContextIngressTrust.Unverified,
        IList<string>? trace = null,
        Func<string, Exception?>? restoreFailure = null)
        => new(
            key,
            trust,
            static value => value.Value,
            static captured => new SecondContext(captured),
            trace,
            restoreFailure: restoreFailure);

    private static KoanContextCarrierRegistry Registry(params IKoanContextCarrier[] carriers)
        => new(carriers);

    [Fact]
    public void Empty_registry_has_allocation_free_capture_and_restore_paths()
    {
        var registry = Registry();
        registry.Capture().Should().BeNull();
        registry.Restore(null, ContextIngressTrust.HostTrusted).Dispose(); // JIT/warm the empty path.

        var beforeCapture = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
            registry.Capture();

        var captureBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCapture;
        captureBytes.Should().Be(0);

        var beforeRestore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1_000; i++)
            registry.Restore(null, ContextIngressTrust.HostTrusted).Dispose();

        (GC.GetAllocatedBytesForCurrentThread() - beforeRestore).Should().Be(0);
    }

    [Fact]
    public void Capture_is_sparse_and_orders_axis_keys_ordinally()
    {
        var registry = Registry(
            Second("zeta"),
            First("alpha"));

        using (KoanContext.Push(new FirstContext("a")))
        using (KoanContext.Push(new SecondContext("z")))
        {
            var captured = registry.Capture();

            captured.Should().NotBeNull();
            captured!.Keys.Should().Equal("alpha", "zeta");
            captured["alpha"].Should().Be("a");
            captured["zeta"].Should().Be("z");
        }

        using (KoanContext.Push(new FirstContext("only")))
            registry.Capture()!.Keys.Should().Equal("alpha");
    }

    [Fact]
    public void Descriptors_are_value_free_and_ordered_for_safe_inspection()
    {
        var registry = Registry(
            Second("zeta", ContextIngressTrust.HostTrusted),
            First("alpha", ContextIngressTrust.Authenticated));

        registry.Descriptors.Should().Equal(
            new KoanContextCarrierRegistry.CarrierDescriptor("alpha", ContextIngressTrust.Authenticated),
            new KoanContextCarrierRegistry.CarrierDescriptor("zeta", ContextIngressTrust.HostTrusted));
    }

    [Fact]
    public void Carrier_identity_and_trust_are_frozen_at_host_composition()
    {
        var carrier = new MutableDeclarationCarrier();
        var registry = Registry(carrier);

        carrier.AxisKey = "test:changed";
        carrier.MinimumIngressTrust = ContextIngressTrust.Unverified;

        registry.Descriptors.Should().ContainSingle().Which.Should().Be(
            new KoanContextCarrierRegistry.CarrierDescriptor("test:stable", ContextIngressTrust.Authenticated));
        registry.Capture()!.Keys.Should().Equal("test:stable");

        var restore = () => registry.Restore(
            new Dictionary<string, string> { ["test:stable"] = "value" },
            ContextIngressTrust.Unverified);
        restore.Should().Throw<KoanContextCarrierException>()
            .Which.Failure.Should().Be(KoanContextCarrierException.FailureKind.InsufficientIngressTrust);
    }

    [Fact]
    public void Capture_returns_null_when_registered_carriers_have_no_value()
        => Registry(First(), Second()).Capture().Should().BeNull();

    [Fact]
    public void Duplicate_axis_keys_fail_with_a_typed_composition_error()
    {
        var act = () => Registry(First("test:duplicate"), Second("test:duplicate"));

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.DuplicateAxis);
        failure.AxisKeys.Should().Contain("test:duplicate");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("BAD")]
    [InlineData("bad\nsecret-value")]
    public void Invalid_axis_keys_fail_without_echoing_the_invalid_value(string axisKey)
    {
        var act = () => Registry(First(axisKey));

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.InvalidAxis);
        failure.Message.Should().NotContain("secret-value");
    }

    [Fact]
    public void Unknown_captured_axis_fails_before_any_restore_or_suppression()
    {
        var carrier = First();
        var registry = Registry(carrier);
        var captured = new Dictionary<string, string> { ["test:unknown"] = "value" };

        var act = () => registry.Restore(captured, ContextIngressTrust.HostTrusted);

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.UnknownAxis);
        failure.AxisKeys.Should().Contain("test:unknown");
        carrier.RestoreCalls.Should().Be(0);
        carrier.SuppressCalls.Should().Be(0);
        KoanContext.Get<FirstContext>().Should().BeNull();
    }

    [Fact]
    public void Unknown_axis_diagnostics_are_bounded_even_for_an_oversized_bag()
    {
        var captured = Enumerable.Range(0, 32)
            .ToDictionary(index => $"test:unknown-{index:D2}", _ => "opaque", StringComparer.Ordinal);
        Action restore = () => Registry().Restore(captured, ContextIngressTrust.HostTrusted);

        var failure = restore.Should().Throw<KoanContextCarrierException>().Which;

        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.UnknownAxis);
        failure.AxisKeys.Should().HaveCount(16);
        failure.Message.Should().NotContain("test:unknown-31");
    }

    [Fact]
    public void Trust_is_checked_for_every_captured_axis_before_any_scope_is_pushed()
    {
        var ordinary = First("alpha");
        var sensitive = Second("secure", ContextIngressTrust.Authenticated);
        var registry = Registry(ordinary, sensitive);
        var captured = new Dictionary<string, string>
        {
            ["alpha"] = "accepted-if-reached",
            ["secure"] = "protected"
        };

        var act = () => registry.Restore(captured, ContextIngressTrust.Unverified);

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.InsufficientIngressTrust);
        failure.RequiredTrust.Should().Be(ContextIngressTrust.Authenticated);
        failure.ProvidedTrust.Should().Be(ContextIngressTrust.Unverified);
        ordinary.RestoreCalls.Should().Be(0);
        sensitive.RestoreCalls.Should().Be(0);
        KoanContext.Get<FirstContext>().Should().BeNull();
        KoanContext.Get<SecondContext>().Should().BeNull();
    }

    [Fact]
    public void Host_trusted_ingress_satisfies_an_authenticated_carrier()
    {
        var registry = Registry(First(trust: ContextIngressTrust.Authenticated));
        var captured = new Dictionary<string, string> { ["test:first"] = "restored" };

        using (registry.Restore(captured, ContextIngressTrust.HostTrusted))
            KoanContext.Get<FirstContext>().Should().Be(new FirstContext("restored"));

        KoanContext.Get<FirstContext>().Should().BeNull();
    }

    [Fact]
    public void Authenticated_ingress_does_not_satisfy_a_host_trusted_carrier()
    {
        var registry = Registry(First(trust: ContextIngressTrust.HostTrusted));
        var captured = new Dictionary<string, string> { ["test:first"] = "protected" };
        Action restore = () => registry.Restore(captured, ContextIngressTrust.Authenticated);

        var failure = restore.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.InsufficientIngressTrust);
        failure.RequiredTrust.Should().Be(ContextIngressTrust.HostTrusted);
        failure.ProvidedTrust.Should().Be(ContextIngressTrust.Authenticated);
    }

    [Fact]
    public void Missing_registered_axes_are_explicitly_suppressed_then_outer_values_restore()
    {
        var first = First();
        var second = Second();
        var registry = Registry(first, second);

        using (KoanContext.Push(new FirstContext("outer-first")))
        using (KoanContext.Push(new SecondContext("outer-second")))
        {
            var captured = new Dictionary<string, string> { ["test:first"] = "incoming-first" };
            using (registry.Restore(captured, ContextIngressTrust.HostTrusted))
            {
                KoanContext.Get<FirstContext>().Should().Be(new FirstContext("incoming-first"));
                KoanContext.Get<SecondContext>().Should().BeNull();
            }

            KoanContext.Get<FirstContext>().Should().Be(new FirstContext("outer-first"));
            KoanContext.Get<SecondContext>().Should().Be(new SecondContext("outer-second"));
        }

        first.RestoreCalls.Should().Be(1);
        second.SuppressCalls.Should().Be(1);
    }

    [Fact]
    public void Null_bag_suppresses_every_registered_axis_instead_of_inheriting_worker_context()
    {
        var registry = Registry(First(), Second());

        using (KoanContext.Push(new FirstContext("worker-first")))
        using (KoanContext.Push(new SecondContext("worker-second")))
        using (registry.Restore(null, ContextIngressTrust.Unverified))
        {
            KoanContext.Get<FirstContext>().Should().BeNull();
            KoanContext.Get<SecondContext>().Should().BeNull();
        }
    }

    [Fact]
    public void Restore_is_axis_sorted_and_disposes_scopes_in_reverse_order()
    {
        var trace = new List<string>();
        var registry = Registry(
            Second("zeta", trace: trace),
            First("alpha", trace: trace));
        var captured = new Dictionary<string, string> { ["zeta"] = "z", ["alpha"] = "a" };

        using (registry.Restore(captured, ContextIngressTrust.HostTrusted))
            trace.Should().Equal("restore:alpha", "restore:zeta");

        trace.Should().Equal("restore:alpha", "restore:zeta", "dispose:zeta", "dispose:alpha");
    }

    [Fact]
    public void Partial_restore_is_unwound_when_a_later_carrier_rejects_its_payload()
    {
        var trace = new List<string>();
        var registry = Registry(
            First("alpha", trace: trace),
            Second(
                "beta",
                trace: trace,
                restoreFailure: _ => KoanContextCarrierException.MalformedPayload("beta")));
        var captured = new Dictionary<string, string> { ["alpha"] = "a", ["beta"] = "bad" };

        var act = () => registry.Restore(captured, ContextIngressTrust.HostTrusted);

        act.Should().Throw<KoanContextCarrierException>()
            .Which.Failure.Should().Be(KoanContextCarrierException.FailureKind.MalformedPayload);
        trace.Should().Equal("restore:alpha", "restore:beta", "dispose:alpha");
        KoanContext.Get<FirstContext>().Should().BeNull();
    }

    [Theory]
    [InlineData(false, KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData(true, KoanContextCarrierException.FailureKind.UnsupportedVersion)]
    public void Carrier_payload_refusals_are_typed_and_never_echo_payloads(
        bool unsupportedVersion,
        KoanContextCarrierException.FailureKind expected)
    {
        const string payload = "v999:super-secret-value";
        var registry = Registry(First(
            restoreFailure: _ => unsupportedVersion
                ? KoanContextCarrierException.UnsupportedVersion("test:first")
                : KoanContextCarrierException.MalformedPayload("test:first")));

        var act = () => registry.Restore(
            new Dictionary<string, string> { ["test:first"] = payload },
            ContextIngressTrust.HostTrusted);

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(expected);
        failure.Message.Should().NotContain(payload);
        failure.InnerException.Should().BeNull();
    }

    [Fact]
    public void Unexpected_restore_failure_is_sanitized_without_payload_or_inner_exception()
    {
        const string payload = "private-payload";
        var registry = Registry(First(
            restoreFailure: captured => new InvalidOperationException($"failed on {captured}")));

        var act = () => registry.Restore(
            new Dictionary<string, string> { ["test:first"] = payload },
            ContextIngressTrust.HostTrusted);

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.RestoreFailed);
        failure.Message.Should().NotContain(payload);
        failure.InnerException.Should().BeNull();
    }

    [Fact]
    public void Unexpected_capture_failure_is_sanitized_without_context_value_or_inner_exception()
    {
        const string privateValue = "private-context-value";
        var registry = Registry(First(
            captureFailure: () => new InvalidOperationException(privateValue)));

        var act = registry.Capture;

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.CaptureFailed);
        failure.Message.Should().NotContain(privateValue);
        failure.InnerException.Should().BeNull();
    }

    [Fact]
    public void Carrier_declared_failures_are_recreated_without_mutable_exception_metadata()
    {
        const string secret = "private-carrier-payload";
        var poisoned = KoanContextCarrierException.MalformedPayload("test:spoofed");
        poisoned.Data["payload"] = secret;
        poisoned.HelpLink = "https://invalid.example/" + secret;
        poisoned.Source = secret;
        var registry = Registry(First(restoreFailure: _ => poisoned));

        Action restore = () => registry.Restore(
            new Dictionary<string, string> { ["test:first"] = "opaque" },
            ContextIngressTrust.HostTrusted);

        var failure = restore.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.MalformedPayload);
        failure.AxisKeys.Should().Equal("test:first");
        failure.Data.Count.Should().Be(0);
        failure.HelpLink.Should().BeNull();
        failure.Source.Should().NotBe(secret);
        failure.Message.Should().NotContain(secret).And.NotContain("test:spoofed");
        failure.InnerException.Should().BeNull();
    }

    [Fact]
    public void Carrier_declared_capture_failures_are_recreated_as_safe_capture_failures()
    {
        const string secret = "private-capture-payload";
        var poisoned = KoanContextCarrierException.MalformedPayload("test:spoofed");
        poisoned.Data["payload"] = secret;
        poisoned.HelpLink = secret;
        poisoned.Source = secret;
        var registry = Registry(First(captureFailure: () => poisoned));
        Func<IReadOnlyDictionary<string, string>?> capture = registry.Capture;

        var failure = capture.Should().Throw<KoanContextCarrierException>().Which;

        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.CaptureFailed);
        failure.AxisKeys.Should().Equal("test:first");
        failure.Data.Count.Should().Be(0);
        failure.HelpLink.Should().BeNull();
        failure.Source.Should().NotBe(secret);
        failure.Message.Should().NotContain(secret).And.NotContain("test:spoofed");
    }

    [Fact]
    public void Normal_scope_disposal_continues_in_reverse_then_surfaces_a_safe_failure()
    {
        const string secret = "private-disposal-failure";
        var trace = new List<string>();
        var registry = Registry(
            new DisposalFailureCarrier("alpha", trace, secret),
            Second("zeta", trace: trace));
        var scope = registry.Restore(
            new Dictionary<string, string> { ["alpha"] = "a", ["zeta"] = "z" },
            ContextIngressTrust.HostTrusted);

        Action dispose = scope.Dispose;
        var failure = dispose.Should().Throw<KoanContextCarrierException>().Which;

        trace.TakeLast(2).Should().Equal("dispose:zeta", "dispose:alpha");
        failure.Failure.Should().Be(KoanContextCarrierException.FailureKind.ScopeDisposalFailed);
        failure.AxisKeys.Should().Equal("alpha");
        failure.Message.Should().NotContain(secret);
        failure.InnerException.Should().BeNull();
        dispose.Should().NotThrow("composite context scopes are idempotent after the first disposal attempt");
    }

    [Fact]
    public async Task One_registry_restores_independent_values_in_parallel_flows()
    {
        var registry = Registry(First());

        async Task<string?> Observe(string value)
        {
            using (registry.Restore(
                       new Dictionary<string, string> { ["test:first"] = value },
                       ContextIngressTrust.HostTrusted))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                return KoanContext.Get<FirstContext>()?.Value;
            }
        }

        var values = await Task.WhenAll(Observe("a"), Observe("b"), Observe("c"));

        values.OrderBy(static value => value).Should().Equal("a", "b", "c");
        KoanContext.Get<FirstContext>().Should().BeNull();
    }
}
