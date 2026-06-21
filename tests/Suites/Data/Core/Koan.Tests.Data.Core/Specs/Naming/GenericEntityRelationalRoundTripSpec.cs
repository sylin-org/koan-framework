using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Tests.Data.Core.Support;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// DATA-0104 canon (ARCH-0079). A generic-over-entity type — the exact shape of the AI pillar's
/// <c>EmbeddingState&lt;Todo&gt; : Entity&lt;EmbeddingState&lt;TEntity&gt;&gt;</c> that <c>[Embedding(Async=true)]</c>
/// persists — must round-trip through real <c>AddKoan()</c> on the relational (SQLite) adapter. Before the
/// recursive grammar fix the table name came from the mangled <c>Type.FullName</c>
/// (<c>Tracker`1[[…, Version=…]]</c> — bracket/backtick make it an illegal SQLite identifier), so
/// <c>[Embedding(Async=true)]</c> crashed at boot. This proves the satellite now creates + round-trips, and that
/// distinct closures are distinct physical tables (no cross-entity contamination).
/// </summary>
public sealed class GenericEntityRelationalRoundTripSpec
{
    public sealed class NamingTodo : Entity<NamingTodo> { }
    public sealed class NamingOrder : Entity<NamingOrder> { }

    // Same structural shape as EmbeddingState<TEntity>; pinned to the relational adapter under test.
    [DataAdapter("sqlite")]
    public sealed class Tracker<T> : Entity<Tracker<T>> where T : class { public string? Note { get; set; } }

    [Fact]
    public async Task Generic_entity_creates_and_round_trips_on_sqlite_without_collision()
    {
        await using var runtime = await DataCoreRuntimeFixture.CreateAsync(includeSqlite: true);
        runtime.BindHost();

        // Pre-fix: this CREATE TABLE failed — the name was the bracket-mangled Type.FullName.
        await new Tracker<NamingTodo> { Id = "a", Note = "todo-tracked" }.Save();
        // Same Id, different closure — must land in a DISTINCT physical table, not clobber the first.
        await new Tracker<NamingOrder> { Id = "a", Note = "order-tracked" }.Save();

        (await Tracker<NamingTodo>.Get("a"))!.Note.Should().Be("todo-tracked",
            "the generic satellite must create + round-trip on the relational adapter");
        (await Tracker<NamingOrder>.Get("a"))!.Note.Should().Be("order-tracked",
            "distinct closures must not collide into one physical table");
    }
}
