using AwesomeAssertions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Cross-adapter ORACLE for the managed-field isolation seam (DATA-0105 §3b, the tenancy substrate; ARCH-0079).
/// Registers a GENERIC (non-tenant) managed field <c>__scope</c> and proves, through the real adapter, that the
/// invisible discriminator the framework injects into the persisted record and filters on actually isolates:
/// reads, the get-by-id IDOR defence, the conflict-aware write verify (no cross-scope row takeover), and scoped
/// explicit deletion. An adapter passes this iff it announces <see cref="DataCaps.Isolation.RowScoped"/> AND its write
/// (conflict-aware upsert) + read (managed-field pushdown) honour the scope — the exact contract the tenant
/// <c>AssertNoTenantLeak</c> rides. Generic so the seam is validated independent of the tenancy module.
/// </summary>
public static class ManagedFieldNoLeak
{
    private static readonly AsyncLocal<string?> _scope = new();

    public sealed class ScopedDoc : Entity<ScopedDoc> { public string Title { get; set; } = ""; }

    private static IDisposable Scope(string id)
    {
        var prev = _scope.Value;
        _scope.Value = id;
        return new Pop(() => _scope.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    /// <summary>Declares the managed discriminator while the owning host is composing.</summary>
    public static void Declare() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__scope",
        ClrType: typeof(string),
        ValueProvider: () => _scope.Value,
        AppliesTo: static type => type == typeof(ScopedDoc),
        RequiredCapability: DataCaps.Isolation.RowScoped));

    /// <summary>Runs the no-leak matrix against the host that already consumed <see cref="Declare"/>.</summary>
    public static async Task AssertNoLeakAsync()
    {
        ManagedFieldRegistry.ForType(typeof(ScopedDoc)).Should().ContainSingle(descriptor =>
            descriptor.StorageName == "__scope" && descriptor.RequiredCapability == DataCaps.Isolation.RowScoped,
            "the shared host must declare its managed field inside AddKoan(...) composition");
        try
        {
            using var _part = EntityContext.Partition("mfnl-" + Guid.CreateVersion7().ToString("n"));

            ScopedDoc a, b;
            using (Scope("A")) a = await new ScopedDoc { Title = "a" }.Save();
            using (Scope("B")) b = await new ScopedDoc { Title = "b" }.Save();

            // Read isolation.
            using (Scope("A")) (await ScopedDoc.All()).Select(d => d.Id).Should().Equal(a.Id);
            using (Scope("B")) (await ScopedDoc.All()).Select(d => d.Id).Should().Equal(b.Id);

            // get-by-id IDOR.
            using (Scope("A")) (await ScopedDoc.Get(b.Id)).Should().BeNull();
            using (Scope("B")) (await ScopedDoc.Get(a.Id)).Should().BeNull();

            // Conflict-aware write verify — A cannot take over B's row by id.
            using (Scope("A"))
            {
                var act = async () => await new ScopedDoc { Id = b.Id, Title = "hijack" }.Save();
                await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cross-scope write*");
            }
            using (Scope("B")) (await ScopedDoc.Get(b.Id))!.Title.Should().Be("b");

            // Scoped explicit delete. G-09 proves isolation; provider-bounded mass deletion is a separate B/P claim.
            using (Scope("A")) await a.Remove();
            using (Scope("A")) (await ScopedDoc.All()).Should().BeEmpty();
            using (Scope("B")) (await ScopedDoc.All()).Select(d => d.Id).Should().Equal(b.Id);
        }
        finally
        {
            _scope.Value = null;
        }
    }
}
