using AwesomeAssertions;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Cross-adapter ORACLE for identity sets larger than any store binds as parameters in one statement (DATA-0162). A
/// <c>Get</c> and a <c>Remove</c> over tens of thousands of ids answer the same with and without a read scope: the
/// scope never turns a working call into one past the store's parameter limit. A query's In and Nin over the same set
/// work too.
/// </summary>
public static class LargeIdentitySets
{
    /// <summary>More ids than SQLite (32,766) or PostgreSQL (65,535) binds as parameters in one statement.</summary>
    public const int Count = 70_000;

    private const string Field = "__bulk";
    private static readonly AsyncLocal<string?> _scope = new();

    public sealed class PlainBulkDoc : Entity<PlainBulkDoc> { public int Ordinal { get; set; } }

    public sealed class ScopedBulkDoc : Entity<ScopedBulkDoc> { public int Ordinal { get; set; } }

    /// <summary>Declares the managed field that read-scopes <see cref="ScopedBulkDoc"/> while the owning host composes.</summary>
    public static void Declare() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: Field,
        ClrType: typeof(string),
        ValueProvider: () => _scope.Value,
        AppliesTo: static type => type == typeof(ScopedBulkDoc),
        RequiredCapability: DataCaps.Isolation.RowScoped));

    /// <summary>Without a read scope, every id is read back in order and every row is removed.</summary>
    public static async Task AssertUnscopedAsync()
    {
        using var _part = EntityContext.Partition("bulk-" + Guid.CreateVersion7().ToString("n"));
        var rows = Enumerable.Range(0, Count).Select(static i => new PlainBulkDoc { Id = Id(i), Ordinal = i }).ToList();
        (await PlainBulkDoc.UpsertMany(rows)).Should().Be(Count);

        var read = await PlainBulkDoc.Get(rows.Select(static row => row.Id));
        read.Should().HaveCount(Count);
        read.Select(static row => row?.Ordinal).Should().Equal(Enumerable.Range(0, Count).Select(static i => (int?)i));

        // The same set as a query's In and Nin: the store's translator carries it, whatever its size.
        var ids = rows.Select(static row => row.Id).ToList();
        (await PlainBulkDoc.Query(row => ids.Contains(row.Id))).Should().HaveCount(Count);
        (await PlainBulkDoc.Query(row => !ids.Contains(row.Id))).Should().BeEmpty();

        (await PlainBulkDoc.Remove(rows.Select(static row => row.Id))).Should().Be(Count);
        (await PlainBulkDoc.Get(Id(0))).Should().BeNull();
        (await PlainBulkDoc.Get(Id(Count - 1))).Should().BeNull();
    }

    /// <summary>Under a read scope, every id the scope owns is read back and removed, and another scope's row is
    /// neither read nor removed.</summary>
    public static async Task AssertScopedAsync()
    {
        try
        {
            using var _part = EntityContext.Partition("bulk-scoped-" + Guid.CreateVersion7().ToString("n"));
            var rows = Enumerable.Range(0, Count).Select(static i => new ScopedBulkDoc { Id = Id(i), Ordinal = i }).ToList();
            var foreign = new ScopedBulkDoc { Id = "foreign", Ordinal = -1 };
            using (Scope("A")) (await ScopedBulkDoc.UpsertMany(rows)).Should().Be(Count);
            using (Scope("B")) await foreign.Save();

            var asked = rows.Select(static row => row.Id).Append(foreign.Id).ToList();
            using (Scope("A"))
            {
                var read = await ScopedBulkDoc.Get(asked);
                read.Should().HaveCount(Count + 1);
                read.Take(Count).Select(static row => row?.Ordinal)
                    .Should().Equal(Enumerable.Range(0, Count).Select(static i => (int?)i));
                read[^1].Should().BeNull("scope A cannot read scope B's row");

                (await ScopedBulkDoc.Remove(asked)).Should().Be(Count, "scope A removes only its own rows");
                (await ScopedBulkDoc.Get(Id(0))).Should().BeNull();
                (await ScopedBulkDoc.Get(Id(Count - 1))).Should().BeNull();
            }
            using (Scope("B")) (await ScopedBulkDoc.Get(foreign.Id)).Should().NotBeNull("scope A's remove must not reach it");
        }
        finally
        {
            _scope.Value = null;
        }
    }

    private static string Id(int i) => $"bulk-{i:D6}";

    private static IDisposable Scope(string id)
    {
        var previous = _scope.Value;
        _scope.Value = id;
        return new Pop(() => _scope.Value = previous);
    }

    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }
}
