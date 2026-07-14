using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Extensions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// Redesign gap B — the cache scope-key convergence + the out-of-band evict bug
/// (<c>docs/architecture/cache-scope-key-convergence.md</c>). The managed equality scope (the tenant
/// discriminator) lived in the cache key on ONLY the read path; <c>Uncache</c> / <c>Flush</c> built a
/// scope-less, partition-less <c>{Type}:{Id}</c> key and silently no-op'd. These specs — through a real
/// <c>AddKoan()</c> boot (ARCH-0079) on SQLite with <c>Koan.Tenancy</c> referenced — prove the evict path now
/// builds the SAME key the read path cached under: a same-tenant <c>Uncache</c>/<c>Flush</c> removes the scoped
/// entry, a cross-tenant evict leaves the other tenant's entry intact, and a <c>[HostScoped]</c> non-axis entity
/// is evicted via the partition-aware key (the universal <c>{Partition}</c> fix). The expected keys are
/// constructed here from the documented contract string, independent of the production builder.
/// </summary>
public sealed class CacheEvictKeyConvergenceSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Posture"] = posture };

    // The read-path cache-key contract: {TypeName}:{Partition}:{Id}[::__koan_tenant={tenant}].
    private static CacheKey ScopedKey<T>(string partition, object id, string tenant)
        => new($"{CacheKey.EntityTypeName(typeof(T))}:{partition}:{id}::__koan_tenant={tenant}");

    private static CacheKey BaseKey<T>(string partition, object id)
        => new($"{CacheKey.EntityTypeName(typeof(T))}:{partition}:{id}");

    // A [Cacheable] tenant entity: the equality __koan_tenant axis partitions its cache key.
    [Cacheable(300)]
    public sealed class EvictNote : Entity<EvictNote> { public string Title { get; set; } = ""; }

    // A [Cacheable] entity exempt from the tenant axis: its key carries no scope segment, only {Partition}.
    [HostScoped]
    [Cacheable(300)]
    public sealed class HostEvictNote : Entity<HostEvictNote> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "cache evict convergence: Uncache under the same tenant removes the scoped entry (was a silent no-op)")]
    public async Task Uncache_under_the_same_tenant_evicts_the_scoped_entry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        var client = runtime.Services.GetRequiredService<ICacheClient>();
        var opts = new CacheEntryOptions();
        var partition = "p" + Guid.CreateVersion7().ToString("n");
        using var _iso = EntityContext.Partition(partition);

        EvictNote n;
        using (Tenant.Use("acme"))
        {
            n = await new EvictNote { Title = "v1" }.Save();   // GetOrSet write populates the scoped cache key
            await EvictNote.Get(n.Id);                          // belt-and-suspenders prime
        }

        var key = ScopedKey<EvictNote>(partition, n.Id, "acme");
        (await client.Exists(key, opts, default)).Should().BeTrue("the scoped entry is cached after a write+read under acme");

        using (Tenant.Use("acme")) await n.Uncache();

        (await client.Exists(key, opts, default)).Should().BeFalse("Uncache under the same tenant must remove the scoped entry");
    }

    [Fact(DisplayName = "cache evict convergence: Uncache under one tenant does not touch another tenant's cached entry")]
    public async Task Uncache_does_not_touch_another_tenants_cached_entry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        var client = runtime.Services.GetRequiredService<ICacheClient>();
        var opts = new CacheEntryOptions();
        var partition = "p" + Guid.CreateVersion7().ToString("n");
        using var _iso = EntityContext.Partition(partition);

        EvictNote a, b;
        using (Tenant.Use("acme")) { a = await new EvictNote { Title = "a" }.Save(); await EvictNote.Get(a.Id); }
        using (Tenant.Use("globex")) { b = await new EvictNote { Title = "b" }.Save(); await EvictNote.Get(b.Id); }

        var acmeKey = ScopedKey<EvictNote>(partition, a.Id, "acme");
        var globexKey = ScopedKey<EvictNote>(partition, b.Id, "globex");
        (await client.Exists(acmeKey, opts, default)).Should().BeTrue();
        (await client.Exists(globexKey, opts, default)).Should().BeTrue();

        using (Tenant.Use("acme")) await a.Uncache();

        (await client.Exists(acmeKey, opts, default)).Should().BeFalse("acme's entry is evicted under acme's scope");
        (await client.Exists(globexKey, opts, default)).Should().BeTrue("globex's entry is untouched by acme's Uncache");
    }

    [Fact(DisplayName = "cache evict convergence: typed cache handle Flush under the same tenant removes the scoped entry")]
    public async Task Flush_under_the_same_tenant_evicts_the_scoped_entry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        var client = runtime.Services.GetRequiredService<ICacheClient>();
        var opts = new CacheEntryOptions();
        var partition = "p" + Guid.CreateVersion7().ToString("n");
        using var _iso = EntityContext.Partition(partition);

        EvictNote n;
        using (Tenant.Use("acme")) { n = await new EvictNote { Title = "v1" }.Save(); await EvictNote.Get(n.Id); }

        var key = ScopedKey<EvictNote>(partition, n.Id, "acme");
        (await client.Exists(key, opts, default)).Should().BeTrue();

        using (Tenant.Use("acme")) await EntityCacheExtensions.Cache<EvictNote, string>().Flush(n.Id);

        (await client.Exists(key, opts, default)).Should().BeFalse("Flush under the same tenant must remove the scoped entry");
    }

    [Fact(DisplayName = "cache evict convergence: Uncache/Flush on a default (unset) id is an idempotent no-op, not a throw")]
    public async Task Uncache_and_Flush_on_a_default_id_are_a_noop()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        // A freshly-constructed, unsaved entity has a null (string) Id — routing through CacheKey.For would throw
        // ArgumentNullException; the evict sites guard the default key and no-op (parity with the write path).
        (await new EvictNote().Uncache()).Should().BeFalse();
        (await EntityCacheExtensions.Cache<EvictNote, string>().Flush(null!)).Should().BeFalse();
    }

    [Fact(DisplayName = "cache evict convergence: a non-axis [HostScoped] [Cacheable] entity is evicted via the partition-aware key")]
    public async Task Uncache_evicts_a_non_axis_entity_via_the_partition_aware_key()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        var client = runtime.Services.GetRequiredService<ICacheClient>();
        var opts = new CacheEntryOptions();
        var partition = "p" + Guid.CreateVersion7().ToString("n");
        using var _iso = EntityContext.Partition(partition);

        // [HostScoped] ⇒ the tenant axis does not apply ⇒ the key carries no scope segment, only {Partition}.
        var n = await new HostEvictNote { Title = "v1" }.Save();
        await HostEvictNote.Get(n.Id);

        var key = BaseKey<HostEvictNote>(partition, n.Id);
        (await client.Exists(key, opts, default)).Should().BeTrue();

        await n.Uncache();

        (await client.Exists(key, opts, default)).Should().BeFalse("the partition-aware evict key now matches the cached key (the universal {Partition} fix)");
    }
}
