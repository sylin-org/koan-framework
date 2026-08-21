using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Instructions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// A declared <c>[Index]</c> has to become an index the optimizer will actually choose. SQL Server indexes the
/// persisted computed column it already holds for a mapped scalar, and substitutes it for the matching
/// <c>JSON_VALUE</c> expression without the query naming either (PMC-041).
/// </summary>
public sealed class SqlServerMappedIndexSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<SqlServerFixture>(fixture, output)
{
    [Fact(DisplayName = "SQL Server: a declared index is built over the column its reads resolve through")]
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

        await using var verify = new SqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);

        await using (var definition = verify.CreateCommand())
        {
            definition.CommandText = """
                SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                  FROM sys.indexes AS i
                  JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                  JOIN sys.columns AS c ON c.object_id = i.object_id AND c.column_id = ic.column_id
                 WHERE i.name = @name
                """;
            definition.Parameters.AddWithValue("name", "ix_sqlserver_probe_status_due");
            var columns = await definition.ExecuteScalarAsync(TestContext.Current.CancellationToken) as string;
            columns.Should().NotBeNull("the declared index must exist, not merely be planned");
            columns.Should().Be("Status,DueAt", "the index keys are the computed columns reads resolve through");
        }

        // Cost preference is a function of table size, and this table holds one row. The claim is narrower and
        // is the thing that was broken: the optimizer can satisfy this read from the index without the query
        // naming the index or the computed column it sits on.
        await using var explain = verify.CreateCommand();
        explain.CommandText = """
            SET SHOWPLAN_TEXT ON;
            """;
        await explain.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var plan = verify.CreateCommand();
        plan.CommandText = """
            SELECT [Id]
            FROM [dbo].[KOAN_MAPPED_INDEX_PROBE] WITH (INDEX(ix_sqlserver_probe_status_due))
            WHERE TRY_CONVERT(bigint, JSON_VALUE([Json], '$."Status"')) = 1
            """;
        var steps = new List<string>();
        await using (var reader = await plan.ExecuteReaderAsync(TestContext.Current.CancellationToken))
        {
            do
            {
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                    steps.Add(reader.GetString(0));
            } while (await reader.NextResultAsync(TestContext.Current.CancellationToken));
        }

        await using (var off = verify.CreateCommand())
        {
            off.CommandText = "SET SHOWPLAN_TEXT OFF;";
            await off.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        Output.WriteLine(string.Join(Environment.NewLine, steps));
        steps.Should().Contain(step => step.Contains(
            "ix_sqlserver_probe_status_due",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "SQL Server: indexing a text property does not cap what that property can hold")]
    public async Task A_declared_index_over_text_does_not_break_long_values()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // JSON_VALUE yields nvarchar(4000) and a nonclustered key tops out at 1700 bytes, so an index built
        // without thinking about it accepts short rows and rejects long ones - a failure that appears in
        // production and never in a test that writes "probe-1". Measured before it was closed: a 2000-character
        // label failed the insert with "index entry of length 4000 bytes exceeds the maximum length of 1700".
        var text = new string('x', 2000);
        var write = () => new TextProbe { Id = "long", Label = text }.Save();

        await write.Should().NotThrowAsync("an index must never narrow what the property accepts");
        (await TextProbe.Get("long"))!.Label.Should().Be(text);

        // Declined, not silently skipped: the store cannot key free text, and the application is told so.
        await using var verify = new SqlConnection(Fixture.ConnectionString);
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var probe = verify.CreateCommand();
        probe.CommandText = "SELECT COUNT(1) FROM sys.indexes WHERE name = @name";
        probe.Parameters.AddWithValue("name", "ix_sqlserver_probe_label");
        Convert.ToInt32(await probe.ExecuteScalarAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        var report = await host.Services.GetRequiredService<IDataService>()
            .Execute<TextProbe, string, IReadOnlyDictionary<string, object?>>(
                new Instruction(RelationalInstructions.SchemaValidate));
        report["State"].Should().Be("Degraded", "a declared index this store cannot key is unproved, not absent");
        ((string[])report["Findings"]!).Should().Contain(finding =>
            finding.Contains("IndexKey:ix_sqlserver_probe_label", StringComparison.Ordinal));
    }

    [Storage(Name = "KOAN_MAPPED_TEXT_PROBE")]
    private sealed class TextProbe : Entity<TextProbe>
    {
        [Index(Name = "ix_sqlserver_probe_label")]
        public string Label { get; set; } = "";
    }

    [Storage(Name = "KOAN_MAPPED_INDEX_PROBE")]
    private sealed class IndexedProbe : Entity<IndexedProbe>
    {
        [Index(Name = "ix_sqlserver_probe_status_due", Order = 0)]
        public int Status { get; set; }

        [Index(Name = "ix_sqlserver_probe_status_due", Order = 1)]
        public DateTimeOffset DueAt { get; set; }
    }
}
