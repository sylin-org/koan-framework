using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// ARCH-0098 / DATA-0105 phase 0 — the "open slot" on the write-stamp stage: external
/// <see cref="WriteStampContributor"/>s composed into <see cref="StorageWritePlan"/> in stable priority order, with
/// the byte-identical off-gate intact. Validated with a generic (non-classification) contributor so the seam is
/// proven independent of any axis. Pins: contributor joins + runs, per-type applicability (Build → null),
/// priority ordering (stable sort, not registration order), identical batch semantics, idempotent
/// registration, and plan-memo invalidation on registration.
/// </summary>
public sealed class WriteContributorSpec : IDisposable
{
    private readonly IDisposable _composition;

    public WriteContributorSpec() => _composition = KoanCompositionScope.Enter(new ServiceCollection());
    public void Dispose() => _composition.Dispose();

    private sealed class Doc : Entity<Doc, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
        public string Body { get; set; } = "";
    }

    private sealed class Other : Entity<Other, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
    }

    private sealed class Seq : Entity<Seq, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
        public List<int> Order { get; } = new();
    }

    /// <summary>Appends "|tag" to a <see cref="Doc"/>'s body, so its run is observable.</summary>
    private sealed class TagStamp(int priority) : IWriteStamp
    {
        public int Priority => priority;
        public void Apply(object entity) { if (entity is Doc d) d.Body += "|tag"; }
    }

    /// <summary>Records its own priority into a <see cref="Seq"/>'s order list, so the apply sequence is observable.</summary>
    private sealed class RecordStamp(int priority) : IWriteStamp
    {
        public int Priority => priority;
        public void Apply(object entity) { if (entity is Seq s) s.Order.Add(priority); }
    }

    private static void RegisterFor<T>(string id, Func<IWriteStamp> stamp)
        => StorageWriteContributorRegistry.Register(new WriteStampContributor(id, t => t == typeof(T) ? stamp() : null));

    [Fact]
    public void Off_path_is_byte_identical_no_contributor_runs()
    {
        StorageWriteContributorRegistry.IsEmpty.Should().BeTrue();
        var doc = new Doc();
        StorageWritePlan.For(typeof(Doc)).ApplyAll(doc);
        doc.Id.Should().NotBeNullOrWhiteSpace();   // identity still stamps
        doc.Body.Should().BeEmpty();               // nothing else
    }

    [Fact]
    public void Registered_contributor_joins_the_plan_and_runs()
    {
        RegisterFor<Doc>("tag", () => new TagStamp(200));
        StorageWriteContributorRegistry.IsEmpty.Should().BeFalse();

        var doc = new Doc();
        StorageWritePlan.For(typeof(Doc)).ApplyAll(doc);
        doc.Body.Should().Be("|tag");
        doc.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Contributor_that_returns_null_does_not_apply_to_that_type()
    {
        RegisterFor<Doc>("tag", () => new TagStamp(200));   // applies only to Doc

        var other = new Other();
        StorageWritePlan.For(typeof(Other)).ApplyAll(other);   // Build(Other) → null
        other.Id.Should().NotBeNullOrWhiteSpace();             // identity only; no throw, no tag
    }

    [Fact]
    public void Priority_orders_the_apply_sequence_not_registration_order()
    {
        // Register out of priority order; the plan must sort stably by Priority.
        RegisterFor<Seq>("late", () => new RecordStamp(200));
        RegisterFor<Seq>("early", () => new RecordStamp(50));

        var seq = new Seq();
        StorageWritePlan.For(typeof(Seq)).ApplyAll(seq);
        seq.Order.Should().Equal(50, 200);   // identity (0) ran first but records nothing; then 50, then 200
    }

    [Fact]
    public void Batch_path_runs_the_same_contributor_plan()
    {
        RegisterFor<Doc>("tag", () => new TagStamp(200));

        var doc = new Doc();
        StorageWritePlan.For(typeof(Doc)).ApplyBatch(doc);
        doc.Id.Should().NotBeNullOrWhiteSpace();
        doc.Body.Should().Be("|tag");
    }

    [Fact]
    public void Registration_is_idempotent_by_id()
    {
        RegisterFor<Doc>("tag", () => new TagStamp(200));
        RegisterFor<Doc>("tag", () => new TagStamp(200));   // duplicate id → no-op
        StorageWriteContributorRegistry.All.Count(c => c.Id == "tag").Should().Be(1);
    }

    [Fact]
    public void Composition_changes_are_visible_before_the_host_plan_is_frozen()
    {
        // Hostless composition deliberately performs no runtime memoization.
        var before = new Doc();
        StorageWritePlan.For(typeof(Doc)).ApplyAll(before);
        before.Body.Should().BeEmpty();

        // A declaration made before host freeze is therefore visible to the compiled runtime plan.
        RegisterFor<Doc>("tag", () => new TagStamp(200));
        var after = new Doc();
        StorageWritePlan.For(typeof(Doc)).ApplyAll(after);
        after.Body.Should().Be("|tag");
    }

    [Fact]
    public void Register_rejects_an_empty_id()
    {
        var act = () => StorageWriteContributorRegistry.Register(new WriteStampContributor("", _ => null));
        act.Should().Throw<ArgumentException>();
    }
}
