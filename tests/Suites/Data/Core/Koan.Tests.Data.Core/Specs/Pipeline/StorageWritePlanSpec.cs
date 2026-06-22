using System;
using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// DATA-0105 phase 1: the Write-stamp stage re-homed onto <see cref="StorageWritePlan"/>. Identity and
/// <c>[Timestamp]</c> are now built-in write-stamps; the plan is memoized per type; the full set runs on
/// Upsert/UpsertMany and the batch subset runs on the batch path — preserving the shipped invariant that batch
/// writes are NOT timestamp-stamped.
/// </summary>
public class StorageWritePlanSpec
{
    private sealed class Stamped : Entity<Stamped, string>
    {
        [Identifier] public override string Id { get; set; } = default!;
        public string Title { get; set; } = "";
        [Timestamp] public DateTimeOffset CreatedAt { get; set; }
        [Timestamp(OnSave = true)] public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class GuidKeyed : Entity<GuidKeyed, Guid>
    {
        [Identifier] public override Guid Id { get; set; }
    }

    [Fact]
    public void ApplyAll_generates_a_missing_string_id()
    {
        var e = new Stamped();
        StorageWritePlan.For(typeof(Stamped)).ApplyAll(e);
        e.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ApplyAll_generates_a_missing_guid_id()
    {
        var e = new GuidKeyed();
        StorageWritePlan.For(typeof(GuidKeyed)).ApplyAll(e);
        e.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void ApplyAll_does_not_overwrite_an_existing_id()
    {
        var e = new Stamped { Id = "fixed" };
        StorageWritePlan.For(typeof(Stamped)).ApplyAll(e);
        e.Id.Should().Be("fixed");
    }

    [Fact]
    public void ApplyAll_stamps_both_timestamps()
    {
        var e = new Stamped();
        StorageWritePlan.For(typeof(Stamped)).ApplyAll(e);
        e.CreatedAt.Should().NotBe(default);
        e.UpdatedAt.Should().NotBe(default);
    }

    [Fact]
    public void ApplyAll_set_once_preserves_CreatedAt_but_updates_UpdatedAt()
    {
        var plan = StorageWritePlan.For(typeof(Stamped));
        var e = new Stamped();
        plan.ApplyAll(e);
        var created = e.CreatedAt;
        var firstUpdated = e.UpdatedAt;

        System.Threading.Thread.Sleep(5);
        plan.ApplyAll(e);

        e.CreatedAt.Should().Be(created);          // set-once
        e.UpdatedAt.Should().BeOnOrAfter(firstUpdated); // on-save
    }

    [Fact]
    public void ApplyBatch_generates_the_id_but_does_not_stamp_timestamps()
    {
        // The shipped BatchFacade invariant: batch writes get an id but no [Timestamp] stamping.
        var e = new Stamped();
        StorageWritePlan.For(typeof(Stamped)).ApplyBatch(e);

        e.Id.Should().NotBeNullOrWhiteSpace();
        e.CreatedAt.Should().Be(default);
        e.UpdatedAt.Should().Be(default);
    }

    [Fact]
    public void Plan_is_memoized_per_type()
    {
        StorageWritePlan.For(typeof(Stamped)).Should().BeSameAs(StorageWritePlan.For(typeof(Stamped)));
    }
}
