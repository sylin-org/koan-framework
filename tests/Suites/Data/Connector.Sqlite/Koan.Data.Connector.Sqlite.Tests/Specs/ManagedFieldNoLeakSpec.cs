using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// DATA-0105 §3b — the managed-field seam proven end-to-end on the real SQLite adapter (no-Docker, real
/// <c>AddKoan()</c>, ARCH-0079), with a <b>generic (non-tenant)</b> managed field so the seam is validated
/// independent of any axis. This is the structural ancestor of the tenant <c>AssertNoTenantLeak</c> proof
/// (phase 4): isolation, the get-by-id IDOR defence, the conflict-aware write verify, scoped RemoveAll, and the
/// no-scope (host) pass-through — all through the invisible <c>__scope</c> discriminator the framework injects
/// into the <c>(Id, Json)</c> envelope and filters on, never a POCO property.
/// </summary>
public sealed class ManagedFieldNoLeakSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output), IDisposable
{
    private static readonly AsyncLocal<string?> _scope = new();

    public void Dispose() => ManagedFieldRegistry.Reset();

    private static IDisposable Scope(string id)
    {
        var prev = _scope.Value;
        _scope.Value = id;
        return new Pop(() => _scope.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private static void EnsureRegistered() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__scope",
        ClrType: typeof(string),
        ValueProvider: () => _scope.Value,
        AppliesTo: t => t == typeof(Doc),
        RequiredCapability: DataCaps.Isolation.RowScoped));

    public sealed class Doc : Entity<Doc>
    {
        public string Title { get; set; } = "";
    }

    [Fact(DisplayName = "Sqlite: a managed scope isolates reads, key-gets (IDOR), and RemoveAll; no scope sees all")]
    public async Task Managed_scope_isolates_reads_and_deletes()
    {
        RequireBackingStore();
        EnsureRegistered();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        Doc a, b;
        using (Scope("A")) a = await new Doc { Title = "a" }.Save();
        using (Scope("B")) b = await new Doc { Title = "b" }.Save();

        // Read isolation — each scope sees only its own rows.
        using (Scope("A")) (await Doc.All()).Select(d => d.Id).Should().Equal(a.Id);
        using (Scope("B")) (await Doc.All()).Select(d => d.Id).Should().Equal(b.Id);

        // get-by-id IDOR — a cross-scope key read returns null (not-found), never another scope's row.
        using (Scope("A")) (await Doc.Get(b.Id)).Should().BeNull();
        using (Scope("B")) (await Doc.Get(a.Id)).Should().BeNull();

        // No scope (host) sees everything — the off/host path is unfiltered, byte-identical.
        (await Doc.All()).Select(d => d.Id).Should().BeEquivalentTo(new[] { a.Id, b.Id });

        // RemoveAll under scope A wipes ONLY A's rows; B's survive (never an unscoped truncate).
        using (Scope("A")) await Doc.RemoveAll();
        using (Scope("A")) (await Doc.All()).Should().BeEmpty();
        using (Scope("B")) (await Doc.All()).Select(d => d.Id).Should().Equal(b.Id);
    }

    [Fact(DisplayName = "Sqlite: a cross-scope id-keyed upsert is rejected (no row takeover)")]
    public async Task Cross_scope_upsert_takeover_is_rejected()
    {
        RequireBackingStore();
        EnsureRegistered();
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        Doc b;
        using (Scope("B")) b = await new Doc { Title = "owned-by-b" }.Save();

        // Scope A tries to overwrite B's row by its id → conflict-aware upsert rejects it.
        using (Scope("A"))
        {
            var act = async () => await new Doc { Id = b.Id, Title = "hijacked" }.Save();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cross-scope write*");
        }

        // B's row is untouched.
        using (Scope("B"))
        {
            var still = await Doc.Get(b.Id);
            still.Should().NotBeNull();
            still!.Title.Should().Be("owned-by-b");
        }
    }
}
