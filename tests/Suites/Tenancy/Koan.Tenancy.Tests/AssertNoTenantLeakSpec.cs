using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Tenancy.Tests.Support;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

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
        => new Dictionary<string, string?> { ["Koan:Tenancy:Posture"] = posture };

    // A fresh storage partition per test isolates each test's rows from other tests/runs sharing the engine
    // (ARCH-0091); the tenant axis is orthogonal and still does the in-test isolation.
    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    public sealed class Note : Entity<Note> { public string Title { get; set; } = ""; }

    [HostScoped]
    public sealed class SystemFlag : Entity<SystemFlag> { public string Name { get; set; } = ""; }

    // ARCH-0100: the generic, tenancy-free exemption marker — infra entities (e.g. the Koan.Jobs ledger) use it
    // instead of [HostScoped] so they never take a Koan.Tenancy dependency. It must earn the SAME exemption.
    public sealed class InfraRow : Entity<InfraRow>, IAmbientExempt { public string Name { get; set; } = ""; }

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

    [Fact(DisplayName = "no tenant leak: the generalized DataAxis.AssertNoLeak<T> (ARCH-0101 §10) proves the whole matrix for the tenant axis")]
    public async Task Generalized_AssertNoLeak_proves_the_tenant_axis()
    {
        // The flagship re-expressed: ONE call rides every plane of the matrix for the tenant axis (read · IDOR ·
        // async-hop carrier round-trip · scoped delete) — the exact proof a future Moderation axis earns identically.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        await DataAxis.AssertNoLeak<Note, string>(Tenant.Use, "acme", "globex");
        // CachedNote additionally exercises the [Cacheable] cache-key partition leg.
        await DataAxis.AssertNoLeak<CachedNote, string>(Tenant.Use, "acme", "globex");
    }

    [Fact(DisplayName = "no tenant leak: scoped DeleteMany and DeleteAll never cross the tenant boundary")]
    public async Task Scoped_delete_paths_are_isolated()
    {
        // DATA-0106 step 6 — previously only RemoveAll was asserted, so a DeleteMany/DeleteAll regression was silent.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        Note a1, a2, b1;
        using (Tenant.Use("acme")) { a1 = await new Note { Title = "a1" }.Save(); a2 = await new Note { Title = "a2" }.Save(); }
        using (Tenant.Use("globex")) b1 = await new Note { Title = "b1" }.Save();

        // DeleteMany under acme over a MIX of acme + globex ids deletes only acme's; globex's id is silently not owned.
        using (Tenant.Use("acme")) (await Note.Remove(new[] { a1.Id, b1.Id })).Should().Be(1);
        using (Tenant.Use("globex")) (await Note.Get(b1.Id)).Should().NotBeNull();   // globex untouched
        using (Tenant.Use("acme")) (await Note.Get(a1.Id)).Should().BeNull();        // acme's deleted

        // DeleteAll under acme wipes only acme's remaining rows — never the unscoped Clear instruction across tenants.
        using (Tenant.Use("acme")) await Data<Note, string>.DeleteAll();
        using (Tenant.Use("acme")) (await Note.All()).Should().BeEmpty();
        using (Tenant.Use("globex")) (await Note.All()).Select(n => n.Id).Should().Equal(b1.Id);
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

    [Fact(DisplayName = "no tenant leak: an IAmbientExempt entity is exempt (same as [HostScoped]) — no Koan.Tenancy dependency")]
    public async Task Ambient_exempt_entity_is_not_tenant_isolated()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        // The generic marker writes without a tenant (the guard exempts it; the managed field does not apply) and
        // is visible under every tenant — proving the exemption predicate unions [HostScoped] OR IAmbientExempt.
        var row = await new InfraRow { Name = "infra" }.Save();

        using (Tenant.Use("acme")) (await InfraRow.Get(row.Id)).Should().NotBeNull();
        using (Tenant.Use("globex")) (await InfraRow.Get(row.Id)).Should().NotBeNull();
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

    [Fact(DisplayName = "no tenant leak: opaque instructions fail before they can clear another tenant's rows")]
    public async Task Opaque_instruction_fails_closed_under_a_tenant_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        Note row;
        using (Tenant.Use("globex")) row = await new Note { Title = "keep" }.Save();

        using (Tenant.Use("acme"))
        {
            var act = async () => await Data<Note, string>.Execute<int>(new Instruction(DataInstructions.Clear));
            await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*compiled segmentation guarantee*");
        }

        using (Tenant.Use("globex")) (await Note.Get(row.Id)).Should().NotBeNull();
    }

    [Fact(DisplayName = "no tenant leak: Direct rejects a segmented operation before opening a provider connection")]
    public async Task Direct_fails_closed_under_a_tenant_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        using (Tenant.Use("acme"))
        {
            var direct = runtime.Services.GetRequiredService<IDataService>().Direct(adapter: "sqlite");
            var act = async () => await direct.Scalar<long>("SELECT 1");
            await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Direct data operations*compiled segmentation guarantee*");
        }
    }

    [Fact(DisplayName = "no tenant leak: a tenant-scoped write fails closed on a non-isolating adapter (fake-noniso)")]
    public async Task Tenant_scoped_write_fails_closed_on_a_non_isolating_adapter()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), adapter: "fake-noniso");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        // The fake adapter does not announce Isolation.RowScoped → a tenant-scoped op fails closed rather than leak.
        using (Tenant.Use("acme"))
        {
            var act = async () => await new Note { Title = "x" }.Save();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not announce*");
        }
    }

    [Fact(DisplayName = "no tenant leak: the un-scoped path under Open resolves to the dev tenant (coherent, isolated)")]
    public async Task Open_unscoped_path_resolves_to_the_dev_tenant()
    {
        // ARCH-0099 §1: there is no Off state. On a Development host the auto-seed runs and an unset ambient scope
        // falls back to the dev tenant — so the write is stamped with the dev tenant and the read filters to it. The
        // un-scoped path is therefore coherent (both rows belong to the same dev tenant and are mutually visible),
        // not unfiltered. Cross-tenant isolation of the fallback is proven in TenancyDevAutoSeedSpec.
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(environment: "Development");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        var a = await new Note { Title = "a" }.Save();
        var b = await new Note { Title = "b" }.Save();
        (await Note.All()).Select(n => n.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });
    }
}
