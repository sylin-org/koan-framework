using Koan.Data.Abstractions.Annotations;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// NK-056 / DATA-0146: PostgreSQL index names are schema-wide and truncated to 63 bytes, and CREATE INDEX IF NOT EXISTS
/// skips on a name match. A generated name that would be cut gains a hash so it stays distinct, an explicit name is
/// qualified per partition table so every partition keeps its own index, and a collision that remains is refused.
/// Container-backed.
/// </summary>
public sealed class PostgresIndexNameSpec(PostgresFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<PostgresFixture>(fixture, output)
{
    [Fact]
    public async Task An_explicit_unique_index_is_created_for_every_partition_table()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var existing = await IndexNames(nameof(PgNamedIndexProbe), unique: true);
        var first = NewPartition("ux-a");
        var second = NewPartition("ux-b");
        using (Lease(first))
        {
            await new PgNamedIndexProbe { Email = "a@example.test" }.Save();
            await new PgNamedIndexProbe { Email = "b@example.test" }.Save();
        }
        using (Lease(second))
        {
            await new PgNamedIndexProbe { Email = "a@example.test" }.Save();
            await FluentActions.Invoking(() => new PgNamedIndexProbe { Email = "a@example.test" }.Save())
                .Should().ThrowAsync<Exception>("the second partition table holds its own unique index");
        }
        using (Lease(first))
            await FluentActions.Invoking(() => new PgNamedIndexProbe { Email = "a@example.test" }.Save())
                .Should().ThrowAsync<Exception>("the first partition table keeps its unique index");

        var names = (await IndexNames(nameof(PgNamedIndexProbe), unique: true)).Except(existing).ToArray();
        names.Should().HaveCount(2, "one unique index per partition table")
            .And.OnlyContain(name => name.StartsWith("UX_PgNamedIndexProbe_Email_", StringComparison.Ordinal) && name.Length <= 63);
    }

    [Fact]
    public async Task Two_indexes_on_a_clamped_partition_table_stay_distinct()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var existing = await IndexNames(nameof(PgIndexProbeWhoseTypeNameIsLongEnoughToPushGeneratedIndexNamesPast63Bytes));
        using (Lease(NewPartition("a-long-partition-label-that-forces-the-table-name-to-clamp")))
        {
            await new PgIndexProbeWhoseTypeNameIsLongEnoughToPushGeneratedIndexNamesPast63Bytes { Email = "a@example.test", Phone = "1" }.Save();
            await new PgIndexProbeWhoseTypeNameIsLongEnoughToPushGeneratedIndexNamesPast63Bytes { Email = "b@example.test", Phone = "2" }.Save();
        }

        var names = (await IndexNames(nameof(PgIndexProbeWhoseTypeNameIsLongEnoughToPushGeneratedIndexNamesPast63Bytes)))
            .Except(existing).ToArray();
        names.Should().HaveCount(2).And.OnlyContain(name => name.Length <= 63);
        names.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task A_database_with_the_old_index_names_gains_the_new_ones_and_keeps_the_old()
    {
        RequireBackingStore();
        var partition = NewPartition("legacy");
        var existing = await IndexNames(nameof(PgLegacyNamedProbe), unique: true);
        await using (var host = await BootAsync())
        using (Lease(partition))
            await new PgLegacyNamedProbe { Email = "a@example.test" }.Save();

        // The state an earlier release left: the explicit name, unqualified, and no other index.
        var current = (await IndexNames(nameof(PgLegacyNamedProbe), unique: true)).Except(existing).Single();
        current.Should().NotBe("UX_PgLegacyNamedProbe_Email", "a partition table's explicit name is qualified");
        try
        {
            await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
            {
                await connection.OpenAsync();
                var definition = await Scalar(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = @name", current);
                await Execute(connection, $"DROP INDEX \"{current}\"");
                await Execute(connection, definition.Replace($"\"{current}\"", "\"UX_PgLegacyNamedProbe_Email\"", StringComparison.Ordinal));
            }
            (await IndexNames(nameof(PgLegacyNamedProbe), unique: true)).Except(existing).Should().Equal("UX_PgLegacyNamedProbe_Email");

            await using (var host = await BootAsync())
            using (Lease(partition))
                await FluentActions.Invoking(() => new PgLegacyNamedProbe { Email = "a@example.test" }.Save())
                    .Should().ThrowAsync<Exception>("the upgraded table is covered by the new unique index");

            var names = (await IndexNames(nameof(PgLegacyNamedProbe), unique: true)).Except(existing).ToArray();
            names.Should().Contain("UX_PgLegacyNamedProbe_Email", "the old index is left for the operator to drop");
            names.Should().Contain(current, "the qualified index is created on the existing table");
        }
        finally
        {
            // The old name is schema-wide, so leaving it would block the next run's fixture state.
            await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
            await connection.OpenAsync();
            await Execute(connection, "DROP INDEX IF EXISTS \"UX_PgLegacyNamedProbe_Email\"");
        }
    }

    [Fact]
    public async Task Two_entities_that_share_an_explicit_name_are_still_refused()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await new PgSharedNameFirst { Email = $"{Guid.NewGuid():n}@example.test" }.Save();
        await FluentActions.Invoking(() => new PgSharedNameSecond { Email = "a@example.test" }.Save())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*unique per schema*");
    }

    private async Task<string[]> IndexNames(string entity, bool unique = false)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.relname
              FROM pg_index AS x
              JOIN pg_class AS i ON i.oid = x.indexrelid
              JOIN pg_class AS t ON t.oid = x.indrelid
             WHERE strpos(t.relname, @entity) > 0 AND NOT x.indisprimary AND (NOT @unique OR x.indisunique)
             ORDER BY i.relname
            """;
        // A long type name is clamped in the table name, so the leading characters identify the table.
        command.Parameters.AddWithValue("entity", entity[..Math.Min(entity.Length, 40)]);
        command.Parameters.AddWithValue("unique", unique);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        return names.ToArray();
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> Scalar(NpgsqlConnection connection, string sql, string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("name", name);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    public sealed class PgNamedIndexProbe : Entity<PgNamedIndexProbe>
    {
        [Index(Name = "UX_PgNamedIndexProbe_Email", Unique = true)] public string Email { get; set; } = "";
    }

    public sealed class PgLegacyNamedProbe : Entity<PgLegacyNamedProbe>
    {
        [Index(Name = "UX_PgLegacyNamedProbe_Email", Unique = true)] public string Email { get; set; } = "";
    }

    public sealed class PgIndexProbeWhoseTypeNameIsLongEnoughToPushGeneratedIndexNamesPast63Bytes
        : Entity<PgIndexProbeWhoseTypeNameIsLongEnoughToPushGeneratedIndexNamesPast63Bytes>
    {
        [Index] public string Email { get; set; } = "";
        [Index] public string Phone { get; set; } = "";
    }

    public sealed class PgSharedNameFirst : Entity<PgSharedNameFirst>
    {
        [Index(Name = "UX_PgSharedName_Email", Unique = true)] public string Email { get; set; } = "";
    }

    public sealed class PgSharedNameSecond : Entity<PgSharedNameSecond>
    {
        [Index(Name = "UX_PgSharedName_Email", Unique = true)] public string Email { get; set; } = "";
    }
}
