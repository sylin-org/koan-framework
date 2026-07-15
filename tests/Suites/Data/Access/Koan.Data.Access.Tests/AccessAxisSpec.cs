using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Hosting.App;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Two boots (fail-closed vs fail-open posture) manage the process-static AppHost.Current; run them serially so the
// two host fixtures never race on it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Koan.Data.Access.Tests;

/// <summary>An [AccessScoped] entity: reads are narrowed to the ambient subject's "event:" scope tokens, on EventId.</summary>
[AccessScoped("EventId", "event:")]
public sealed class Doc : Entity<Doc>
{
    public string EventId { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>A non-access-scoped entity — the access axis must be a byte-identical no-op for it.</summary>
public sealed class Plain : Entity<Plain>
{
    public string Title { get; set; } = "";
}

/// <summary>Focused contract for Subject's Core-owned logical-flow carrier.</summary>
public sealed class SubjectContextCarrierSpec
{
    private static readonly IKoanContextCarrier Carrier = new SubjectContextCarrier();

    [Fact]
    public void Carrier_has_stable_identity_and_requires_authenticated_ingress()
    {
        Carrier.AxisKey.Should().Be("koan:subject");
        Carrier.MinimumIngressTrust.Should().Be(ContextIngressTrust.Authenticated);
    }

    [Fact]
    public void Subject_is_a_business_facade_over_the_Core_context_value()
    {
        using (Subject.Unconstrained("operator"))
            KoanContext.Get<SubjectContext>().Should().BeSameAs(Subject.Current);

        KoanContext.Get<SubjectContext>().Should().BeNull();
    }

    [Fact]
    public void Capture_preserves_the_existing_v1_wire_encodings()
    {
        Carrier.Capture().Should().BeNull();

        using (Subject.System())
            Carrier.Capture().Should().Be("v1:system");

        using (Subject.Unconstrained("operator"))
            Carrier.Capture().Should().Be("v1:id:operator");

        using (Subject.Use("guest", ["event:E1"]))
            Carrier.Capture().Should().Be("v1:scoped:guest\u001fevent:E1");

        using (Subject.Use("guest", []))
            Carrier.Capture().Should().Be("v1:scoped:guest\u001f");
    }

    [Fact]
    public void Capture_orders_scopes_ordinally_for_stable_wire_identity()
    {
        using (Subject.Use("guest", ["event:Z", "event:A", "event:M"]))
            Carrier.Capture().Should().Be("v1:scoped:guest\u001fevent:A\u001fevent:M\u001fevent:Z");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Subject_ids_cannot_contain_the_wire_separator(bool constrained)
    {
        var enter = () => constrained
            ? Subject.Use("alice\u001fadmin", ["event:E1"])
            : Subject.Unconstrained("alice\u001fadmin");

        enter.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("subjectId");
        Subject.Current.Should().BeNull();
    }

    [Fact]
    public void Subject_scopes_are_not_cast_mutable_after_the_context_is_created()
    {
        using (Subject.Use("guest", ["event:E1"]))
        {
            var exposed = (ISet<string>)Subject.Current!.Scopes!;
            Action add = () => { exposed.Add("event:E2"); };
            Action remove = () => { exposed.Remove("event:E1"); };
            Action clear = exposed.Clear;

            add.Should().Throw<NotSupportedException>();
            remove.Should().Throw<NotSupportedException>();
            clear.Should().Throw<NotSupportedException>();
            Subject.Current.Scopes.Should().Equal("event:E1");
        }
    }

    [Fact]
    public void Capture_then_restore_round_trips_a_constrained_subject()
    {
        string? captured;
        using (Subject.Use("guest", ["event:E1"]))
            captured = Carrier.Capture();

        using (Carrier.Restore(captured!))
        {
            Subject.Current!.Id.Should().Be("guest");
            Subject.Current.IsConstrained.Should().BeTrue();
            Subject.Current.Scopes.Should().Equal("event:E1");
        }

        Subject.Current.Should().BeNull();
    }

    [Fact]
    public void Suppress_clears_worker_context_then_restores_it()
    {
        using (Subject.Unconstrained("worker"))
        {
            using (Carrier.Suppress())
                Subject.Current.Should().BeNull();

            Subject.Current!.Id.Should().Be("worker");
        }
    }

    [Theory]
    [InlineData("v1:id:", KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData("v1:id:alice\u001fadmin", KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData("v1:scoped:\u001fscope", KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData("v1:unknown", KoanContextCarrierException.FailureKind.MalformedPayload)]
    [InlineData("v999:private-payload", KoanContextCarrierException.FailureKind.UnsupportedVersion)]
    public void Restore_fails_closed_with_safe_typed_errors(
        string captured,
        KoanContextCarrierException.FailureKind expected)
    {
        var act = () => Carrier.Restore(captured);

        var failure = act.Should().Throw<KoanContextCarrierException>().Which;
        failure.Failure.Should().Be(expected);
        failure.AxisKeys.Should().Equal("koan:subject");
        failure.Message.Should().NotContain(captured);
        failure.InnerException.Should().BeNull();
        Subject.Current.Should().BeNull();
    }

    [Fact]
    public void Access_module_registers_its_carrier_independently_with_Core()
    {
        var services = new ServiceCollection();
        services.AddKoanCore();
        new Koan.Data.Access.Initialization.KoanAutoRegistrar().Register(services);
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IKoanContextCarrier>()
            .Should().ContainSingle().Which.Should().BeOfType<SubjectContextCarrier>();
    }
}

/// <summary>
/// A tiny subject-observing job, discovered by the same real <c>AddKoan()</c> boot. It records the ambient subject it
/// executed under (proving the <see cref="SubjectContextCarrier"/> rehydrated the submitting subject across the async
/// hop) and the Doc titles a <c>Doc.All()</c> inside the job returns (proving the access axis narrows in a JOB — the
/// exact thing a web-layer hook could never do).
/// </summary>
public sealed class SubjectProbeJob : Entity<SubjectProbeJob>, IKoanJob<SubjectProbeJob>
{
    public static bool Observed;
    public static bool ObservedConstrained;
    public static IReadOnlyList<string> ObservedScopes = Array.Empty<string>();
    public static IReadOnlyList<string> VisibleTitles = Array.Empty<string>();

    public static void Reset()
    {
        Observed = false;
        ObservedConstrained = false;
        ObservedScopes = Array.Empty<string>();
        VisibleTitles = Array.Empty<string>();
    }

    public static async Task Execute(SubjectProbeJob job, JobContext ctx, CancellationToken ct)
    {
        var s = Subject.Current;
        Observed = s is not null;
        ObservedConstrained = s?.IsConstrained ?? false;
        ObservedScopes = s?.Scopes?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        // The job reads in the default partition (Data routing is not carried; only registered Koan context axes are);
        // the test seeds the job's docs in the default partition for exactly this reason.
        VisibleTitles = (await Doc.All()).Select(d => d.Title).OrderBy(t => t).ToList();
    }
}

/// <summary>
/// SEC-0008 — the data-layer access axis flagship. A real <c>AddKoan()</c> boot (ARCH-0079) of the
/// <c>Koan.Data.Access</c> module over the in-memory store proves the four ambient-subject states + the carrier:
/// a CONSTRAINED subject narrows every read (and get-by-id IDOR) to its scope tokens; an UNCONSTRAINED subject and
/// SYSTEM see all; an ABSENT subject fails closed (deny-all); a non-[AccessScoped] entity is a byte-identical no-op;
/// and the subject rides a durable job so a guest-triggered job is inherently scoped. No Docker, plain dotnet test.
/// </summary>
public class AccessHostFixture : IAsyncLifetime
{
    public IntegrationHost Host { get; private set; } = null!;
    private IServiceProvider? _prevAppHost;

    protected virtual Dictionary<string, string?> Settings() => new(StringComparer.Ordinal)
    {
        ["Koan:Environment"] = "Test",
        ["Koan:Data:Sources:Default:Adapter"] = "inmemory",
    };

    public async ValueTask InitializeAsync()
    {
        Host = await KoanIntegrationHost.Configure()
            .WithSettings(Settings())
            .ConfigureServices(s =>
            {
                s.AddKoan();
                s.Configure<JobsOptions>(o => { o.EnableWorker = false; o.RescheduleJitter = TimeSpan.Zero; });
            })
            .StartAsync();

        _prevAppHost = AppHost.Current;
        AppHost.Current = Host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        AppHost.Current = _prevAppHost;
        await Host.DisposeAsync();
    }
}

public sealed class AccessAxisSpec : IClassFixture<AccessHostFixture>
{
    private readonly AccessHostFixture _fx;
    public AccessAxisSpec(AccessHostFixture fx) => _fx = fx;

    // A fresh partition per test isolates the shared in-memory store (no cross-test bleed).
    private static IDisposable Part() => EntityContext.With(partition: Guid.NewGuid().ToString("n"));

    private Task Drain(CancellationToken ct = default)
        => _fx.Host.Services.GetRequiredService<JobOrchestrator>().DrainAsync(ct);

    [Fact(DisplayName = "constrained subject: reads narrow to the granted scope; cross-scope get-by-id is a fail-closed null (IDOR)")]
    public async Task Constrained_subject_narrows_reads_and_get_by_id()
    {
        using var _ = Part();
        var a = await new Doc { EventId = "E1", Title = "a" }.Save();
        var b = await new Doc { EventId = "E2", Title = "b" }.Save();

        // Both physically exist (System sees all).
        using (Subject.System()) (await Doc.All()).Should().HaveCount(2);

        using (Subject.Use("guest", new[] { "event:E1" }))
        {
            (await Doc.All()).Select(d => d.Title).Should().Equal("a");      // only the granted event's photo
            (await Doc.Get(a.Id)).Should().NotBeNull();                      // own row visible
            (await Doc.Get(b.Id)).Should().BeNull();                         // cross-scope IDOR → existence-hiding null
        }
    }

    [Fact(DisplayName = "unconstrained subject (an operator): sees all rows — the axis adds no scope")]
    public async Task Unconstrained_subject_sees_all()
    {
        using var _ = Part();
        await new Doc { EventId = "X", Title = "a" }.Save();
        await new Doc { EventId = "Y", Title = "b" }.Save();

        using (Subject.Unconstrained("operator"))
            (await Doc.All()).Should().HaveCount(2);
    }

    [Fact(DisplayName = "absent subject (default fail-closed): an access-scoped read returns nothing")]
    public async Task Absent_subject_fails_closed()
    {
        using var _ = Part();
        var a = await new Doc { EventId = "X", Title = "a" }.Save();
        await new Doc { EventId = "Y", Title = "b" }.Save();

        using (Subject.System()) (await Doc.All()).Should().HaveCount(2);    // they exist

        // No subject in scope → fail closed (deny-all), for both the collection read and get-by-id.
        (await Doc.All()).Should().BeEmpty();
        (await Doc.Get(a.Id)).Should().BeNull();
    }

    [Fact(DisplayName = "constrained subject with no matching grant sees nothing (empty scope ⇒ deny-all)")]
    public async Task Constrained_with_no_matching_grant_sees_nothing()
    {
        using var _ = Part();
        await new Doc { EventId = "E1", Title = "a" }.Save();

        using (Subject.Use("guest", new[] { "event:OTHER" }))
            (await Doc.All()).Should().BeEmpty();
        using (Subject.Use("guest", Array.Empty<string>()))
            (await Doc.All()).Should().BeEmpty();
    }

    [Fact(DisplayName = "non-[AccessScoped] entity: byte-identical no-op (full read under any/no subject)")]
    public async Task Non_access_scoped_entity_is_unaffected()
    {
        using var _ = Part();
        await new Plain { Title = "a" }.Save();

        (await Plain.All()).Should().HaveCount(1);                            // no subject, yet full read (not opted in)
        using (Subject.Use("guest", new[] { "event:nope" }))
            (await Plain.All()).Should().HaveCount(1);                        // a constrained subject does not narrow it
    }

    [Fact(DisplayName = "the subject rides a durable job: a guest-triggered job is inherently access-scoped")]
    public async Task Subject_carrier_scopes_a_job()
    {
        SubjectProbeJob.Reset();

        // Seed in the DEFAULT partition (Data routing is not carried across the hop; only registered context axes are).
        var stamp = Guid.NewGuid().ToString("n");
        await new Doc { EventId = "jobE1-" + stamp, Title = "seen-" + stamp }.Save();
        await new Doc { EventId = "jobE2-" + stamp, Title = "hidden-" + stamp }.Save();

        using (Subject.Use("guest", new[] { "event:jobE1-" + stamp }))
            await new SubjectProbeJob().Job.Submit();

        await Drain();   // claim + restore the captured subject + execute, on the worker thread

        SubjectProbeJob.Observed.Should().BeTrue();
        SubjectProbeJob.ObservedConstrained.Should().BeTrue();
        SubjectProbeJob.ObservedScopes.Should().Equal("event:jobE1-" + stamp);
        // The access axis narrowed the job's Doc.All() to the granted event — the killer property a web hook can't give.
        SubjectProbeJob.VisibleTitles.Should().Equal("seen-" + stamp);
    }
}

/// <summary>The fail-open posture: an absent subject is a no-op (full read), proving the <see cref="AccessOptions"/> toggle.</summary>
public sealed class AccessFailOpenHostFixture : AccessHostFixture
{
    protected override Dictionary<string, string?> Settings()
    {
        var s = base.Settings();
        s["Koan:Data:Access:FailClosedOnAbsentSubject"] = "false";
        return s;
    }
}

public sealed class AccessFailOpenSpec : IClassFixture<AccessFailOpenHostFixture>
{
    private readonly AccessFailOpenHostFixture _fx;
    public AccessFailOpenSpec(AccessFailOpenHostFixture fx) => _fx = fx;

    private static IDisposable Part() => EntityContext.With(partition: Guid.NewGuid().ToString("n"));

    [Fact(DisplayName = "fail-open posture: an absent subject reads all (the configured opt-out)")]
    public async Task Absent_subject_fail_open_reads_all()
    {
        using var _ = Part();
        await new Doc { EventId = "X", Title = "a" }.Save();
        await new Doc { EventId = "Y", Title = "b" }.Save();

        // No subject + FailClosedOnAbsentSubject=false → the axis imposes no constraint.
        (await Doc.All()).Should().HaveCount(2);

        // A constrained subject is still narrowed regardless of posture.
        using (Subject.Use("guest", new[] { "event:X" }))
            (await Doc.All()).Select(d => d.Title).Should().Equal("a");
    }
}
