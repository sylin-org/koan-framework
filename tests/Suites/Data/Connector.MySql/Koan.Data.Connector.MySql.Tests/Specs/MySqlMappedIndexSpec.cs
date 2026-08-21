using Koan.Data.Abstractions.Annotations;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Tests.Specs;

/// <summary>
/// A declared <c>[Index]</c> has to become an index the optimizer will actually choose. MySQL cannot index a
/// JSON expression directly, so the index sits on the stored generated column this store already holds, and the
/// optimizer substitutes it for the matching expression without the query naming either (PMC-041).
/// </summary>
public sealed class MySqlMappedIndexSpec(MySqlFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MySqlFixture>(fixture, output)
{
    [Fact(DisplayName = "MySQL: a declared index is built over the column its reads resolve through")]
    public async Task Declared_indexes_are_built_and_usable()
    {
        RequireBackingStore();
        await using (var host = await BootAsync())
        {
            await new IndexedProbe
            {
                Id = "probe-1",
                Status = 1,
                DueAt = DateTimeOffset.UtcNow
            }.Save();
        }

        await using var verify = new MySqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);

        await using (var definition = verify.CreateCommand())
        {
            definition.CommandText = """
                SELECT GROUP_CONCAT(column_name ORDER BY seq_in_index)
                  FROM information_schema.statistics
                 WHERE table_schema = DATABASE() AND index_name = @name
                """;
            definition.Parameters.AddWithValue("name", "ix_mysql_probe_status_due");
            var columns = await definition.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
            columns.Should().NotBeNull("the declared index must exist, not merely be planned");
            columns.Should().Be("Status,DueAt", "the index keys are the generated columns reads resolve through");
        }

        await using var explain = verify.CreateCommand();
        explain.CommandText = """
            EXPLAIN SELECT `Id`
            FROM `KOAN_MAPPED_INDEX_PROBE`
            WHERE CAST(JSON_UNQUOTE(JSON_EXTRACT(`Json`, '$."Status"')) AS SIGNED) = 1
            """;
        var used = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                var key = reader.GetOrdinal("key");
                used.Add(reader.IsDBNull(key) ? "<none>" : reader.GetString(key));
            }

        Output.WriteLine(string.Join(", ", used));
        used.Should().Contain(key => key.Contains("ix_mysql_probe_status_due", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "MySQL: indexing a text property does not cap what that property can hold")]
    public async Task A_declared_index_over_text_does_not_break_long_values()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // A mapped string is held as longtext, and MySQL refuses a key over TEXT without a prefix length. The
        // failure is loud rather than silent here, but it would still stop a boot.
        var text = new string('x', 2000);
        var write = () => new TextProbe { Id = "long", Label = text }.Save();

        await write.Should().NotThrowAsync("an index must never narrow what the property accepts");
        (await TextProbe.Get("long"))!.Label.Should().Be(text);

        await using var verify = new MySqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var probe = verify.CreateCommand();
        probe.CommandText = """
            SELECT COUNT(1) FROM information_schema.statistics
             WHERE table_schema = DATABASE() AND index_name = @name
            """;
        probe.Parameters.AddWithValue("name", "ix_mysql_probe_label");
        Convert.ToInt32(await probe.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Storage(Name = "KOAN_MAPPED_TEXT_PROBE")]
    private sealed class TextProbe : Entity<TextProbe>
    {
        [Index(Name = "ix_mysql_probe_label")]
        public string Label { get; set; } = "";
    }

    [Storage(Name = "KOAN_MAPPED_INDEX_PROBE")]
    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_mysql_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_mysql_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
