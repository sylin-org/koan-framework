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

        // The predicate is transcribed rather than generated, so it pins the spelling the filter compiler emits
        // from outside the connector. It has already earned that: changing how the dialect reads a JSON scalar
        // stopped the optimizer substituting the generated column, and this assertion is what said so.
        await using var explain = verify.CreateCommand();
        explain.CommandText = """
            EXPLAIN SELECT `Id`
            FROM `KOAN_MAPPED_INDEX_PROBE`
            WHERE CAST(CASE WHEN JSON_TYPE(JSON_EXTRACT(`Json`, '$."Status"')) = 'NULL' THEN NULL
                            ELSE JSON_UNQUOTE(JSON_EXTRACT(`Json`, '$."Status"')) END AS SIGNED) = 1
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

    [Fact(DisplayName = "MySQL: a text property is indexed by prefix without capping what it can hold")]
    public async Task A_declared_index_over_text_is_built_and_exact()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // A mapped string is held as longtext, and MySQL refuses a key over TEXT without a prefix length. A
        // prefix is not an approximation - the engine seeks it and rechecks the full column - so the property
        // keeps accepting values far longer than the key, and equality on one of them still resolves exactly.
        var shared = new string('x', 2000);
        var text = shared + "-alpha";
        var write = () => new TextProbe { Id = "long", Label = text }.Save();

        await write.Should().NotThrowAsync("an index must never narrow what the property accepts");
        (await TextProbe.Get("long"))!.Label.Should().Be(text);

        // The two labels are identical for their first two thousand characters and differ only past the key
        // prefix, so anything that seeked the prefix and stopped there would return both.
        await new TextProbe { Id = "twin", Label = shared + "-beta" }.Save();
        var matched = await TextProbe.Query(probe => probe.Label == text);
        matched.Should().ContainSingle().Which.Id.Should().Be("long",
            "a prefix seek must be rechecked against the whole column");

        await using var verify = new MySqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var definition = verify.CreateCommand();
        definition.CommandText = """
            SELECT sub_part FROM information_schema.statistics
             WHERE table_schema = DATABASE() AND index_name = @name
            """;
        definition.Parameters.AddWithValue("name", "ix_mysql_probe_label");
        var prefix = await definition.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        prefix.Should().NotBeNull("the declared index over text must exist");
        Convert.ToInt32(prefix).Should().Be(255, "a TEXT key is taken by prefix");
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
