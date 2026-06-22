using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0095 / DATA-0105 §3b — the flagship <b>AssertNoTenantLeak</b> proof, through a real <c>AddKoan()</c>
/// boot (ARCH-0079) on the no-Docker SQLite adapter (which announces <c>Isolation.RowScoped</c>) with the
/// <c>Koan.Tenancy</c> module referenced. Two tenants exercise every plane the managed <c>__koan_tenant</c>
/// discriminator must cover: read isolation, the get-by-id IDOR defence, the conflict-aware write verify (no
/// row takeover), scoped <c>RemoveAll</c>, the <c>[HostScoped]</c> exemption, the <c>[Cacheable]</c> cache-key
/// partition, raw-path fail-closed, the non-isolating-adapter fail-closed, and the Open un-scoped path (unstamped).
/// The discriminator is never a POCO property — it is the invisible field the framework injects and filters.
/// </summary>
public sealed class AssertNoTenantLeakSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Posture"] = posture };

    // A fresh storage partition per test isolates each test's rows from other tests/runs sharing the engine
    // (ARCH-0091); the tenant axis is orthogonal and still does the in-test isolation.
    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    public sealed class Note : Entity<Note> { public string Title { get; set; } = ""; }

    [HostScoped]
    public sealed class SystemFlag : Entity<SystemFlag> { public string Name { get; set; } = ""; }

    [Cacheable(300)]
    public sealed class CachedNote : Entity<CachedNote> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "no tenant leak: reads/key-gets/deletes are isolated; a cross-tenant upsert cannot take over a row")]
    public async Task Tenant_isolation_holds_for_reads_writes_and_deletes()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        Note a, b;
        using (Tenant.Use("acme")) a = await new Note { Title = "acme-secret" }.Save();
        using (Tenant.Use("globex")) b = await new Note { Title = "globex-secret" }.Save();

        // Read isolation — each tenant sees only its own rows.
        using (Tenant.Use("acme")) (await Note.All()).Select(n => n.Id).Should().Equal(a.Id);
        using (Tenant.Use("globex")) (await Note.All()).Select(n => n.Id).Should().Equal(b.Id);

        // get-by-id IDOR — a cross-tenant key read returns null (not-found), never the other tenant's row.
        using (Tenant.Use("acme")) (await Note.Get(b.Id)).Should().BeNull();
        using (Tenant.Use("globex")) (await Note.Get(a.Id)).Should().BeNull();

        // Write verify — acme cannot overwrite globex's row by guessing its id (conflict-aware upsert rejects it).
        using (Tenant.Use("acme"))
        {
            var act = async () => await new Note { Id = b.Id, Title = "hijacked" }.Save();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cross-scope write*");
        }
        using (Tenant.Use("globex")) (await Note.Get(b.Id))!.Title.Should().Be("globex-secret"); // untouched

        // RemoveAll under acme wipes ONLY acme's rows — never an unscoped truncate.
        using (Tenant.Use("acme")) await Note.RemoveAll();
        using (Tenant.Use("acme")) (await Note.All()).Should().BeEmpty();
        using (Tenant.Use("globex")) (await Note.All()).Select(n => n.Id).Should().Equal(b.Id);
    }

    [Fact(DisplayName = "no tenant leak: a [HostScoped] entity is exempt — visible under every tenant")]
    public async Task HostScoped_entity_is_not_tenant_isolated()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        // [HostScoped] writes without a tenant (the guard exempts it; the managed field does not apply).
        var flag = await new SystemFlag { Name = "maintenance" }.Save();

        using (Tenant.Use("acme")) (await SystemFlag.Get(flag.Id)).Should().NotBeNull();
        using (Tenant.Use("globex")) (await SystemFlag.Get(flag.Id)).Should().NotBeNull();
    }

    [Fact(DisplayName = "no tenant leak: a [Cacheable] entity does not serve one tenant's cached row to another")]
    public async Task Cacheable_entity_does_not_leak_through_the_cache_key()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        CachedNote c;
        using (Tenant.Use("acme")) c = await new CachedNote { Title = "acme-cached" }.Save();
        // Populate the L1 cache under acme.
        using (Tenant.Use("acme")) (await CachedNote.Get(c.Id))!.Title.Should().Be("acme-cached");
        // globex must miss acme's cache entry (the managed scope partitions the cache key) and see nothing.
        using (Tenant.Use("globex")) (await CachedNote.Get(c.Id)).Should().BeNull();
    }

    [Fact(DisplayName = "no tenant leak: a raw query on a tenant entity fails closed under scope (RLS backstop absent)")]
    public async Task Raw_query_fails_closed_under_a_tenant_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        using (Tenant.Use("acme"))
        {
            var act = async () => await Note.QueryRaw("SELECT Id, Json FROM Note");
            await act.Should().ThrowAsync<NotSupportedException>();
        }
    }

    [Fact(DisplayName = "no tenant leak: a tenant-scoped write fails closed on a non-isolating adapter (JSON)")]
    public async Task Tenant_scoped_write_fails_closed_on_a_non_isolating_adapter()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), adapter: "json");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        // JSON does not announce Isolation.RowScoped → a tenant-scoped op fails closed rather than leak.
        using (Tenant.Use("acme"))
        {
            var act = async () => await new Note { Title = "x" }.Save();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not announce*");
        }
    }

    [Fact(DisplayName = "no tenant leak: the un-scoped path under Open is unstamped + unfiltered (byte-identical names)")]
    public async Task Open_unscoped_path_is_unstamped_and_unfiltered()
    {
        // ARCH-0099 §1: there is no Off state. Under dev-open, a tenant-scoped op with NO tenant in scope is
        // warned (not blocked) and the __koan_tenant value provider returns null → the write is unstamped and
        // the read is unfiltered. (The fixture's Test env never triggers the IsDevelopment()-gated auto-seed,
        // so nothing puts a tenant in scope here.) This is the new "zero regression" for the un-scoped path.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Open"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        var a = await new Note { Title = "a" }.Save();
        var b = await new Note { Title = "b" }.Save();
        (await Note.All()).Select(n => n.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });
    }
}
