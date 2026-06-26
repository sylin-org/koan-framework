using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Logging;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Vector.Abstractions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Vector;

/// <summary>
/// ARCH-0103 §6 follow-on — the CollectionName/IndexName pin footgun. A pinned static collection/index name is used
/// verbatim, bypassing the partition + routed-source name-fold, so Container/Database isolation is defeated for that
/// entity. <see cref="VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation{TEntity}"/> surfaces it: it WARNS (the pin is
/// a deliberate, documented override — not a fail-close) but only when it actually bites (a partition or routed source is
/// in scope), and once per entity type. Tenancy (RowScoped) is unaffected (it isolates by a read-filter, not the name),
/// so it must NOT trigger this. Two halves: the pure predicate, and the emission observed via the internal
/// <see cref="KoanLog.TestSink"/> seam (the same chokepoint the AdapterConnectionResolver warner test uses).
/// <para>Each assertion uses its own marker entity type so the static once-per-type dedup never crosses tests.</para>
/// </summary>
public sealed class VectorNamePinWarningSpec
{
    // Distinct marker types — the warner dedups per Type, so reusing one across tests would mask a real second emission.
    private sealed class PredicateProbe { }      // pure predicate has no state ⇒ safe to reuse across the predicate facts
    private sealed class ContainerParticleProbe { }
    private sealed class WarnProbe { }
    private sealed class NoScopeProbe { }
    private sealed class BlankProbe { }

    // A Container-mode [DataAxis] realizes isolation purely as a container-name particle (StorageNameParticleRegistry).
    // This stand-in yields one for a single target type when registered, so the predicate must treat it as a defeat.
    private sealed class FakeContainerParticleContributor(Type appliesTo) : IStorageNameParticleContributor
    {
        public string Axis => "test-container-axis";
        public Particle? GetParticle(Type entityType)
            => entityType == appliesTo ? new Particle(0, Axis, "shard-x") : null;
    }

    // ──────────────────────────── the pure predicate ────────────────────────────

    [Fact]
    public void Predicate_blank_pin_is_never_a_defeat_even_under_a_partition()
    {
        using (EntityContext.Partition("acme"))
        {
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>(null).Should().BeFalse();
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>("").Should().BeFalse();
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>("   ").Should().BeFalse();
        }
    }

    [Fact]
    public void Predicate_pinned_with_no_discriminator_is_harmless()
    {
        // No partition, no routed source ⇒ the pin defeats nothing for this op.
        VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>("pinned").Should().BeFalse();
    }

    [Fact]
    public void Predicate_pinned_with_active_partition_is_a_defeat()
    {
        using (EntityContext.Partition("acme"))
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>("pinned").Should().BeTrue();
    }

    [Fact]
    public void Predicate_pinned_with_explicit_routed_source_is_a_defeat()
    {
        // EntityContext.Source is RouteKind.Explicit; an axis-derived (RouteKind.DatabaseAxis) route resolves through the
        // same RoutedSource.Resolve and yields Kind != None, so the predicate (which is route-kind-agnostic) treats both
        // identically — the explicit case exercises the routed-source branch.
        using (EntityContext.Source("vshard_a"))
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>("pinned").Should().BeTrue();
    }

    [Fact]
    public void Predicate_pinned_with_active_container_axis_particle_is_a_defeat()
    {
        // The name-fold composes THREE discriminators; this is the one the original predicate missed (review HIGH): a
        // Container-mode [DataAxis] realizes isolation as a container-name particle, not via EntityContext.Partition.
        StorageNameParticleRegistry.Register(new FakeContainerParticleContributor(typeof(ContainerParticleProbe)));
        try
        {
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<ContainerParticleProbe>("pinned").Should().BeTrue();
            // Type-scoped: an entity the contributor does not apply to is still harmless.
            VectorAdapterNaming.PinnedNameDefeatsActiveIsolation<PredicateProbe>("pinned").Should().BeFalse();
        }
        finally { StorageNameParticleRegistry.Reset(); }
    }

    // ──────────────────────────── the emission (via TestSink) ────────────────────────────

    [Fact]
    public void Pinned_name_under_a_partition_emits_one_isolation_warning_then_dedups()
    {
        var captured = CaptureFor(nameof(WarnProbe));
        try
        {
            using (EntityContext.Partition("acme"))
            {
                VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation<WarnProbe>("pinned-coll", "CollectionName");
                // Second call for the same type must NOT emit again (once-per-type dedup).
                VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation<WarnProbe>("pinned-coll", "CollectionName");
            }
        }
        finally { KoanLog.TestSink = null; }

        var snapshot = Snapshot(captured);
        snapshot.Should().ContainSingle("the warning fires once and dedups on the second call for the same entity type");
        var entry = snapshot[0];
        entry.Stage.Should().Be(KoanLogStage.Cnfg);
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Outcome.Should().Be("isolation-defeated");

        var ctx = entry.Context.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
        ctx.Should().Contain("entity", nameof(WarnProbe));
        ctx.Should().Contain("option", "CollectionName");
        ctx.Should().Contain("pinnedName", "pinned-coll");
        ctx.Should().Contain("activeDiscriminator", "partition");
        ctx.Should().ContainKey("note");
    }

    [Fact]
    public void Pinned_name_with_no_discriminator_emits_nothing()
    {
        var captured = CaptureFor(nameof(NoScopeProbe));
        try
        {
            // No partition, no routed source ⇒ harmless pin ⇒ no warning.
            VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation<NoScopeProbe>("pinned-coll", "CollectionName");
        }
        finally { KoanLog.TestSink = null; }

        Snapshot(captured).Should().BeEmpty();
    }

    [Fact]
    public void Blank_pin_emits_nothing_even_under_a_partition()
    {
        var captured = CaptureFor(nameof(BlankProbe));
        try
        {
            using (EntityContext.Partition("acme"))
                VectorAdapterNaming.WarnIfPinnedNameDefeatsIsolation<BlankProbe>("   ", "CollectionName");
        }
        finally { KoanLog.TestSink = null; }

        Snapshot(captured).Should().BeEmpty();
    }

    // ──────────────────────────── capture helpers ────────────────────────────

    private static List<(KoanLogStage Stage, LogLevel Level, string? Outcome, (string Key, object? Value)[] Context)>
        CaptureFor(string entityName)
    {
        var captured = new List<(KoanLogStage, LogLevel, string?, (string, object?)[])>();
        KoanLog.TestSink = (stage, level, action, outcome, context) =>
        {
            if (action != "vector.name.pinned") return;
            if (!context.Any(kv => kv.Key == "entity" && string.Equals(kv.Value?.ToString(), entityName))) return;
            lock (captured) captured.Add((stage, level, outcome, context));
        };
        return captured;
    }

    private static List<(KoanLogStage Stage, LogLevel Level, string? Outcome, (string Key, object? Value)[] Context)>
        Snapshot(List<(KoanLogStage, LogLevel, string?, (string, object?)[])> captured)
    {
        lock (captured) return captured.ToList();
    }
}
