using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;
using Koan.Core.Observability.Health;
using Xunit;

namespace Koan.Core.Tests.Diagnostics;

public sealed class KoanRuntimeFactsSpec
{
    [Fact]
    public void Each_store_owns_a_distinct_host_session()
    {
        var first = new KoanRuntimeFactStore();
        var second = new KoanRuntimeFactStore();

        first.Current.SessionId.Should().NotBe(second.Current.SessionId);
        first.Replace([], complete: true).SessionId.Should().Be(first.Current.SessionId);
    }

    [Fact]
    public void Snapshot_is_deterministic_versioned_and_round_trippable()
    {
        var store = new KoanRuntimeFactStore();
        var later = Fact("z.code", KoanFactKind.Guarantee, "z-subject");
        var earlier = Fact("a.code", KoanFactKind.Discovery, "a-subject");

        var snapshot = store.Replace([later, earlier, later], complete: true);

        snapshot.Schema.Should().Be(Constants.Diagnostics.FactSchemaVersion);
        snapshot.Complete.Should().BeTrue();
        snapshot.Facts.Select(fact => fact.Code).Should().Equal("a.code", "z.code");

        var json = KoanFactJson.Serialize(snapshot);
        var roundTrip = KoanFactJson.Deserialize(json);
        roundTrip.Should().NotBeNull();
        roundTrip!.Schema.Should().Be(snapshot.Schema);
        roundTrip.Facts.Select(fact => fact.Id).Should().Equal(snapshot.Facts.Select(fact => fact.Id));
        roundTrip.Facts.Should().Contain(fact => fact.Kind == KoanFactKind.Guarantee);
        roundTrip.Facts.Select(fact => fact.CorrelationId)
            .Should().Equal(snapshot.Facts.Select(fact => fact.CorrelationId));
    }

    [Fact]
    public void Fact_construction_redacts_credentials_and_has_no_payload_bag()
    {
        var fact = KoanFact.Create(
            "test.redaction",
            KoanFactKind.Rejection,
            KoanFactState.Rejected,
            "Server=db;Password=super-secret",
            "Failed at https://alice:password@example.test",
            "test",
            "Set User Id=alice;Pwd=another-secret",
            "spec",
            "redaction");

        var json = KoanFactJson.Serialize(new KoanFactEnvelope(Constants.Diagnostics.FactSchemaVersion, 1, "test", DateTimeOffset.UtcNow, true, [fact]));

        json.Should().NotContain("super-secret");
        json.Should().NotContain("another-secret");
        json.Should().NotContain("alice:password");
        json.Should().NotContain("payload", "the shared fact schema has no arbitrary provider payload");
    }

    [Fact]
    public async Task Health_is_unknown_before_collection_and_degraded_for_reported_failure()
    {
        var store = new KoanRuntimeFactStore();
        var contributor = new KoanFactsHealthContributor(store);

        (await contributor.Check()).State.Should().Be(HealthState.Unknown);

        store.Replace([Fact(
            Constants.Diagnostics.Codes.CollectionFailed,
            KoanFactKind.Degradation,
            "reporter",
            KoanFactState.CollectionFailed)], complete: true);

        var report = await contributor.Check();
        report.State.Should().Be(HealthState.Degraded);
        report.Data!["issues"].Should().Be(1);
    }

    [Fact]
    public void Recorder_updates_the_latest_stable_subject_without_reopening_collection()
    {
        var store = new KoanRuntimeFactStore();
        store.Replace([], complete: true);
        var before = store.Current.Sequence;

        store.Record(Descriptor(KoanFactState.Selected, "first"));
        store.Record(Descriptor(KoanFactState.Rejected, "second"));

        store.Current.Complete.Should().BeTrue();
        store.Current.Sequence.Should().Be(before + 2);
        store.Current.Facts.Should().ContainSingle();
        store.Current.Facts[0].State.Should().Be(KoanFactState.Rejected);
        store.Current.Facts[0].Summary.Should().Be("second");
    }

    [Fact]
    public void Recollection_replaces_collected_facts_without_discarding_operation_facts()
    {
        var store = new KoanRuntimeFactStore();
        store.Replace([Fact("old", KoanFactKind.Discovery, "old")], complete: false);
        store.Record(Descriptor(KoanFactState.Selected, "operation"));

        store.Replace([Fact("new", KoanFactKind.Discovery, "new")], complete: true);

        store.Current.Facts.Select(fact => fact.Code).Should().BeEquivalentTo("new", "test.operation");
        store.Current.Facts.Should().NotContain(fact => fact.Code == "old");
    }

    [Fact]
    public async Task Rejected_capability_decision_is_inspectable_without_degrading_readiness()
    {
        var store = new KoanRuntimeFactStore();
        store.Replace([], complete: true);
        store.Record(Descriptor(KoanFactState.Rejected, "request rejected"));

        var report = await new KoanFactsHealthContributor(store).Check();

        report.State.Should().Be(HealthState.Healthy);
        report.Data!["issues"].Should().Be(0);
    }

    private static KoanFact Fact(
        string code,
        KoanFactKind kind,
        string subject,
        KoanFactState state = KoanFactState.Observed)
        => KoanFact.Create(
            code,
            kind,
            state,
            subject,
            "safe summary",
            "safe-reason",
            null,
            "spec",
            subject,
            DateTimeOffset.UnixEpoch);

    private static KoanFactDescriptor Descriptor(KoanFactState state, string summary)
        => new(
            "test.operation",
            KoanFactKind.Capability,
            state,
            "relationship:parent->child.ParentId",
            summary,
            "test-reason",
            null,
            "spec",
            "request");
}
