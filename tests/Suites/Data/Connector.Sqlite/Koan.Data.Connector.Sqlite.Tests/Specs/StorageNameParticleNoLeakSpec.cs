using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Naming;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Containers;
using Xunit;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>
/// ARCH-0101 §3 — the container-name particle plane proven end-to-end on the real SQLite adapter (real
/// <c>AddKoan()</c>, ARCH-0079). A separate-container axis registers an <see cref="IStorageNameParticleContributor"/>
/// (a LEADING tenant particle, <c>alpha-Doc</c>); the framework routes each tenant's rows to its OWN physical table,
/// so isolation is by CONTAINER, not by row-filter. The pure-registration proof that mode-3 (separate-container)
/// rides the generic seam: no tenancy module, no managed field — just a name-particle contributor.
/// </summary>
public sealed class StorageNameParticleNoLeakSpec(SqliteFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqliteFixture>(fixture, output), IDisposable
{
    private static readonly AsyncLocal<string?> _tenant = new();

    public void Dispose() { _tenant.Value = null; StorageNameParticleRegistry.Reset(); }

    private static IDisposable Tenant(string id)
    {
        var prev = _tenant.Value;
        _tenant.Value = id;
        return new Pop(() => _tenant.Value = prev);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    public sealed class Doc : Entity<Doc> { public string Title { get; set; } = ""; }

    private sealed class TenantNameParticle : IStorageNameParticleContributor
    {
        public string Axis => "tenant";
        public Particle? GetParticle(Type entityType)
            => entityType == typeof(Doc) && _tenant.Value is { } id
                ? new Particle(100, "tenant", id, ParticlePosition.Leading, "-")
                : null;
    }

    [Fact(DisplayName = "Sqlite: a separate-container name-particle axis isolates each tenant's rows by physical container")]
    public async Task Separate_container_axis_isolates_by_physical_container()
    {
        RequireBackingStore();
        StorageNameParticleRegistry.Register(new TenantNameParticle());
        await using var host = await BootAsync();
        using var _ = Lease(NewPartition());

        Doc a, b;
        using (Tenant("alpha")) a = await new Doc { Title = "alpha-doc" }.Save();
        using (Tenant("beta")) b = await new Doc { Title = "beta-doc" }.Save();

        // Each tenant's rows live in its OWN physical table (alpha-Doc#part vs beta-Doc#part) — a read under one
        // tenant never sees the other's, and never an unscoped cross-tenant read. Isolation by CONTAINER.
        using (Tenant("alpha")) (await Doc.All()).Select(d => d.Title).Should().Equal("alpha-doc");
        using (Tenant("beta")) (await Doc.All()).Select(d => d.Title).Should().Equal("beta-doc");

        // Container-level IDOR: a get-by-id under the wrong tenant targets the wrong table ⇒ not found.
        using (Tenant("beta")) (await Doc.Get(a.Id)).Should().BeNull();
        using (Tenant("alpha")) (await Doc.Get(b.Id)).Should().BeNull();

        // The host (no tenant) resolves a THIRD container (Doc#part) and sees neither tenant's rows.
        (await Doc.All()).Should().BeEmpty();
    }
}
