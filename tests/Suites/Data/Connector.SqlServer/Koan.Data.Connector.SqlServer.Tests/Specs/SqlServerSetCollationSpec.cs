using Koan.Core;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// DATA-0162: a large set binds as one JSON text on a source that may create nothing, and its members yield to the column's
/// collation as a lone parameter does.
/// </summary>
public sealed class SqlServerSetCollationSpec(SqlServerFixture fixture)
{
    [Fact(DisplayName = "SQL Server: a large text set on an External source answers what Eq answers under the column's collation")]
    public async Task A_text_set_answers_under_the_column_collation()
    {
        await Execute("""
            DROP TABLE IF EXISTS [dbo].[CS_ROWS];
            CREATE TABLE [dbo].[CS_ROWS] ([ID] bigint PRIMARY KEY, [NAME] nvarchar(200) COLLATE Latin1_General_CS_AS NOT NULL);
            INSERT INTO [dbo].[CS_ROWS] VALUES (1, N'Alpha'), (2, N'alpha'), (3, N'beta');
            """);
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:CaseSensitive:Adapter"] = "sqlserver",
                ["Koan:Data:Sources:CaseSensitive:ConnectionString"] = fixture.ConnectionString,
                ["Koan:Data:Sources:CaseSensitive:Schema"] = "dbo",
                ["Koan:Data:Sources:CaseSensitive:StorageLifecycle"] = nameof(StorageLifecycle.External),
                ["Koan:Data:Sources:CaseSensitive:Access"] = nameof(DataSourceAccess.ReadWrite),
            })
            .ConfigureServices(services => services.AddKoan(koan => koan.Data.Source("CaseSensitive").Map<CaseSensitiveRow>(map => map
                .Container("CS_ROWS").Key(row => row.Id).Name("ID").Property(row => row.Name).Name("NAME"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("CaseSensitive"))
        {
            var eq = (await CaseSensitiveRow.Query(row => row.Name == "alpha")).Select(static row => row.Id).ToArray();
            eq.Should().Equal(2L);
            foreach (var size in new[] { 2, 2_502 })
            {
                var names = new[] { "alpha" }.Concat(Enumerable.Range(0, size - 1).Select(static i => $"pad-{i}")).ToList();
                (await CaseSensitiveRow.Query(row => names.Contains(row.Name))).Select(static row => row.Id)
                    .Should().Equal(eq, $"an In of {size} answers what Eq answers under the column's collation");
            }
        }
    }

    private async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public sealed class CaseSensitiveRow : Entity<CaseSensitiveRow, long> { public string Name { get; set; } = ""; }
}
