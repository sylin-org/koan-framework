using Koan.Core;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Testing.Integration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// DATA-0162: a statement's filter is bound against the measured limit (2,098 parameters) less the parameters the
/// statement binds itself. A conditional replace binds every SET value and the identity, so on a wide row its guard's list
/// stays per member only while the whole fits, and binds as one value beyond — at every size up to and past the limit.
/// </summary>
public sealed class SqlServerStatementBoundSpec(SqlServerFixture fixture)
{
    [Fact(DisplayName = "SQL Server: a conditional replace on a wide row works at every guard size around its exact bound")]
    public async Task A_conditional_replace_on_a_wide_row_works_at_its_exact_bound()
    {
        // The set types live in dbo once a source that may create them has run there.
        await using (var managed = await KoanIntegrationHost.Configure()
                         .WithSettings(fixture.SettingsForBoot())
                         .ConfigureServices(services => services.AddKoan())
                         .StartAsync(TestContext.Current.CancellationToken))
            await new BoundProbe { Name = "creates the set types in dbo" }.Save();

        await Execute("""
            DROP TABLE IF EXISTS [dbo].[WIDE_ROWS];
            CREATE TABLE [dbo].[WIDE_ROWS] ([ID] bigint PRIMARY KEY, [C01] nvarchar(40) NOT NULL, [C02] nvarchar(40) NOT NULL, [C03] nvarchar(40) NOT NULL, [C04] nvarchar(40) NOT NULL, [C05] nvarchar(40) NOT NULL, [C06] nvarchar(40) NOT NULL, [C07] nvarchar(40) NOT NULL, [C08] nvarchar(40) NOT NULL, [C09] nvarchar(40) NOT NULL, [C10] nvarchar(40) NOT NULL, [C11] nvarchar(40) NOT NULL, [C12] nvarchar(40) NOT NULL, [C13] nvarchar(40) NOT NULL, [C14] nvarchar(40) NOT NULL, [C15] nvarchar(40) NOT NULL, [C16] nvarchar(40) NOT NULL, [C17] nvarchar(40) NOT NULL, [C18] nvarchar(40) NOT NULL, [C19] nvarchar(40) NOT NULL);
            INSERT INTO [dbo].[WIDE_ROWS] VALUES (1, N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x', N'x');
            """);
        await using var host = await KoanIntegrationHost.Configure()
            .WithSettings(new Dictionary<string, string?>(fixture.SettingsForBoot(), StringComparer.Ordinal)
            {
                ["Koan:Data:Sources:Wide:Adapter"] = "sqlserver",
                ["Koan:Data:Sources:Wide:ConnectionString"] = fixture.ConnectionString,
                ["Koan:Data:Sources:Wide:Schema"] = "dbo",
                ["Koan:Data:Sources:Wide:StorageLifecycle"] = nameof(StorageLifecycle.External),
                ["Koan:Data:Sources:Wide:Access"] = nameof(DataSourceAccess.ReadWrite),
            })
            .ConfigureServices(services => services.AddKoan(koan => koan.Data.Source("Wide").Map<WideRow>(map => map
                .Container("WIDE_ROWS").Key(row => row.Id).Name("ID")
                .Property(row => row.C01).Name("C01")
                .Property(row => row.C02).Name("C02")
                .Property(row => row.C03).Name("C03")
                .Property(row => row.C04).Name("C04")
                .Property(row => row.C05).Name("C05")
                .Property(row => row.C06).Name("C06")
                .Property(row => row.C07).Name("C07")
                .Property(row => row.C08).Name("C08")
                .Property(row => row.C09).Name("C09")
                .Property(row => row.C10).Name("C10")
                .Property(row => row.C11).Name("C11")
                .Property(row => row.C12).Name("C12")
                .Property(row => row.C13).Name("C13")
                .Property(row => row.C14).Name("C14")
                .Property(row => row.C15).Name("C15")
                .Property(row => row.C16).Name("C16")
                .Property(row => row.C17).Name("C17")
                .Property(row => row.C18).Name("C18")
                .Property(row => row.C19).Name("C19"))))
            .StartAsync(TestContext.Current.CancellationToken);

        using (EntityContext.Source("Wide"))
        {
            var conditional = host.Services.GetRequiredService<IDataService>()
                .GetRepository<WideRow, long>().Should()
                .BeAssignableTo<IConditionalWriteRepository<WideRow, long>>().Which;
            var row = (await WideRow.Get(1))!;
            // Nineteen SET values and one identity: the guard's bound is 2,078. Every size from below it to the measured
            // limit itself, and past it, replaces the row.
            foreach (var size in Enumerable.Range(2_070, 29).Append(2_502))
            {
                var ids = Enumerable.Range(1, size).Select(static i => (long)i).ToList();
                row.C01 = $"claimed at {size}";
                (await conditional.ConditionalReplaceAsync(row, LinqFilterCompiler.Compile<WideRow>(item => ids.Contains(item.Id))))
                    .Should().BeTrue($"a guard of {size} members fits the statement or binds as one value");
            }
        }
        (await Scalar("SELECT [C01] FROM [dbo].[WIDE_ROWS] WHERE [ID] = 1")).Should().Be("claimed at 2502");
    }

    private async Task Execute(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    public sealed class WideRow : Entity<WideRow, long>
    {
        public string C01 { get; set; } = "";
        public string C02 { get; set; } = "";
        public string C03 { get; set; } = "";
        public string C04 { get; set; } = "";
        public string C05 { get; set; } = "";
        public string C06 { get; set; } = "";
        public string C07 { get; set; } = "";
        public string C08 { get; set; } = "";
        public string C09 { get; set; } = "";
        public string C10 { get; set; } = "";
        public string C11 { get; set; } = "";
        public string C12 { get; set; } = "";
        public string C13 { get; set; } = "";
        public string C14 { get; set; } = "";
        public string C15 { get; set; } = "";
        public string C16 { get; set; } = "";
        public string C17 { get; set; } = "";
        public string C18 { get; set; } = "";
        public string C19 { get; set; } = "";
    }

    public sealed class BoundProbe : Entity<BoundProbe> { public string Name { get; set; } = ""; }
}
