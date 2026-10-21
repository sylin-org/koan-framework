using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// NK-056: PostgreSQL index names are schema-wide, and CREATE INDEX IF NOT EXISTS skips on a name match, so an explicit
/// unique index name shared by two partition tables used to leave the second table without its index. Container-backed.
/// </summary>
public sealed class PostgresIndexNameSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task An_explicit_index_name_shared_by_two_partitions_is_refused_not_skipped()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        using (Lease(NewPartition("ux-a")))
            await new PgNamedIndexProbe { Email = "a@example.test" }.Save();
        using (Lease(NewPartition("ux-b")))
            await FluentActions.Invoking(() => new PgNamedIndexProbe { Email = "a@example.test" }.Save())
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*unique per schema*");
    }


    public sealed class PgNamedIndexProbe : Entity<PgNamedIndexProbe>
    {
        [Index(Name = "UX_PgNamedIndexProbe_Email", Unique = true)] public string Email { get; set; } = "";
    }
}
