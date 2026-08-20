using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// Redesign gap B — the cache scope-key convergence + the out-of-band evict bug
/// (<c>docs/architecture/cache-scope-key-convergence.md</c>). The managed equality scope (the tenant
/// discriminator) lived in the cache key on ONLY the read path; the former explicit eviction APIs built a
/// scope-less, partition-less <c>{Type}:{Id}</c> key and silently no-op'd. These specs — through a real
/// <c>AddKoan()</c> boot (ARCH-0079) on SQLite with <c>Koan.Tenancy</c> referenced — prove the evict path
/// consumes the SAME host-owned plan as the read path.
///
/// <para><b>How these prove it.</b> The Cache pillar owns its physical identity encoding and does not expose
/// it, so a spec cannot honestly reconstruct a key and probe <c>ICacheClient</c> for it — an earlier version
/// did, and asserted a lookup the client cannot perform, because <c>ICacheClient</c> takes a literal key and
/// knows nothing about the Entity's policy or ambient scope. The observable contract is
/// <see cref="EntityCacheEviction"/>: <c>Removed</c> counts entries the topology reported <i>present and
/// removed</i>, while <c>Absent</c> counts removal calls that found nothing. A second eviction is therefore a
/// real oracle — it distinguishes "the write path and the evict path agreed on the key" from "evict built a
/// different key and quietly no-op'd", which is precisely the bug this file exists to catch.</para>
/// </summary>
public sealed class CacheEvictKeyConvergenceSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = posture };

    // A [Cacheable] tenant entity: the equality __koan_tenant axis partitions its cache key.
    [Cacheable(300)]
    public sealed class EvictNote : Entity<EvictNote> { public string Title { get; set; } = ""; }

    // A [Cacheable] entity exempt from the tenant axis: its key carries no scope segment, only {Partition}.
    [HostScoped]
    [Cacheable(300)]
    public sealed class HostEvictNote : Entity<HostEvictNote> { public string Title { get; set; } = ""; }

    [Cacheable(300)]
    public sealed class UnsetEvictNote : Entity<UnsetEvictNote, int>;

    [CachePolicy(CacheScope.Entity, "custom:{TypeName}:{Partition}:{Id}", Tags = new[] { nameof(CustomEvictNote) })]
    public sealed class CustomEvictNote : Entity<CustomEvictNote>;

    [Fact(DisplayName = "cache evict convergence: Entity Cache eviction under the same tenant removes the scoped entry")]
    public async Task Entity_cache_eviction_under_the_same_tenant_evicts_the_scoped_entry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

        EvictNote n;
        using (Tenant.Use("acme"))
        {
            n = await new EvictNote { Title = "v1" }.Save();   // GetOrSet write populates the scoped cache key
            await EvictNote.Get(n.Id);                          // belt-and-suspenders prime
        }

        EntityCacheEviction first, second;
        using (Tenant.Use("acme")) first = await n.Cache.Evict();
        using (Tenant.Use("acme")) second = await n.Cache.Evict();

        first.Removed.Should().Be(1, "the evict key must match the key the write path populated under acme");
        second.Removed.Should().Be(0);
        second.Absent.Should().Be(1, "the entry is really gone, so the first call removed rather than no-op'd");
    }

    [Fact(DisplayName = "cache evict convergence: Entity Cache eviction under one tenant does not touch another tenant's cached entry")]
    public async Task Entity_cache_eviction_does_not_touch_another_tenants_cached_entry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

        // One entity, cached under acme. The discriminating move is to evict it while scoped to globex:
        // if the tenant discriminator is part of the cache key, globex's evict addresses a DIFFERENT key,
        // finds nothing, and leaves acme's entry intact. If the discriminator were missing, globex would
        // address acme's entry and remove it.
        //
        // Two tenants cannot simply share a business key here — the data axis rejects that as a cross-scope
        // write — so this asymmetric probe, not a shared id, is what makes the property observable.
        EvictNote note;
        using (Tenant.Use("acme"))
        {
            note = await new EvictNote { Title = "a" }.Save();
            await EvictNote.Get(note.Id);
        }

        EntityCacheEviction fromGlobex, fromAcme;
        using (Tenant.Use("globex")) fromGlobex = await note.Cache.Evict();
        using (Tenant.Use("acme")) fromAcme = await note.Cache.Evict();

        fromGlobex.Removed.Should().Be(0);
        fromGlobex.Absent.Should().Be(1,
            "globex's evict key carries globex's discriminator, so it cannot address acme's cached entry");
        fromAcme.Removed.Should().Be(1,
            "acme's entry survived the cross-tenant eviction attempt, so the tenant discriminator is part of the cache key");
    }

    [Fact(DisplayName = "cache evict convergence: finite Entity Cache eviction removes each scoped entry")]
    public async Task Finite_entity_cache_eviction_removes_each_scoped_entry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

        EvictNote first, second;
        using (Tenant.Use("acme"))
        {
            first = await new EvictNote { Title = "v1" }.Save();
            second = await new EvictNote { Title = "v2" }.Save();
            await EvictNote.Get(first.Id);
            await EvictNote.Get(second.Id);
        }

        EntityCacheEviction eviction, again;
        using (Tenant.Use("acme")) eviction = await new[] { first, second }.Cache.Evict();
        using (Tenant.Use("acme")) again = await new[] { first, second }.Cache.Evict();

        eviction.Removed.Should().Be(2, "each entry in the finite source is addressed by its own scoped key");
        again.Removed.Should().Be(0);
        again.Absent.Should().Be(2, "both entries are really gone");
    }

    [Fact(DisplayName = "cache evict convergence: an unset id is an explicit skip, not a throw")]
    public async Task Unset_id_is_an_explicit_skip()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        var eviction = await new UnsetEvictNote().Cache.Evict();

        eviction.Enumerated.Should().Be(1);
        eviction.Skipped.Should().Be(1);
        eviction.Confirmed.Should().Be(0);
        eviction.SourceCompleted.Should().BeTrue();
    }

    [Fact(DisplayName = "cache evict convergence: a non-axis [HostScoped] [Cacheable] entity is evicted via the partition-aware key")]
    public async Task Entity_cache_eviction_evicts_a_non_axis_entity_via_the_partition_aware_key()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

        // [HostScoped] ⇒ the tenant axis does not apply ⇒ the key carries no scope segment, only {Partition}.
        var n = await new HostEvictNote { Title = "v1" }.Save();
        await HostEvictNote.Get(n.Id);

        var first = await n.Cache.Evict();
        var second = await n.Cache.Evict();

        first.Removed.Should().Be(1, "the partition-aware evict key matches the key the write path populated");
        second.Removed.Should().Be(0);
        second.Absent.Should().Be(1);
    }

    [Fact(DisplayName = "cache evict convergence: a custom Entity key template is shared by repository writes and explicit eviction")]
    public async Task Custom_key_template_is_shared_by_repository_and_explicit_eviction()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

        CustomEvictNote note;
        using (Tenant.Use("acme")) note = await new CustomEvictNote().Save();

        EntityCacheEviction eviction;
        using (Tenant.Use("acme")) eviction = await note.Cache.Evict();

        // Divergent templates between the write path and the evict path would surface here as Absent=1:
        // the removal call would complete having found nothing, which is exactly the original no-op bug.
        eviction.Removed.Should().Be(1, "the repository write and the explicit eviction resolved the same custom template");
        eviction.Absent.Should().Be(0);
    }
}
