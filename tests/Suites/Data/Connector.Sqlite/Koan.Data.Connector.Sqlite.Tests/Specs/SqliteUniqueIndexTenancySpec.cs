using System.Collections.Generic;
using System.Threading.Tasks;
using Koan.Data.AdapterSurface.TestKit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Connector.Sqlite.Tests.Specs;

/// <summary>DATA-0150 on SQLite: a unique index leads with <c>json_extract("Json", '$.__koan_tenant')</c>.</summary>
public sealed class SqliteUniqueIndexTenancySpec(SqliteFixture fixture, ITestOutputHelper output)
    : UniqueIndexTenancySpecsBase<SqliteFixture>(fixture, output)
{
    private const string NamedRoot = "KOAN_CONFORMANCE_NAMED_MEMBER";
    private const string NamedIndex = "ux_conformance_named_code";

    protected override async Task DropReportedIndex(IReadOnlyDictionary<string, object?> report)
    {
        await using var connection = new SqliteConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = (string)report["Drop"]!;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// DATA-0160 rule 2: a database where a build before DATA-0150 and DATA-0160 provisioned a partition table first
    /// holds the declared global unique index under its unqualified name on that table. After an upgrade with row-scoped
    /// tenancy the partition table gains its scoped index, and DATA-0150's warning still names the global one, so the
    /// operator can drop it and one value is accepted in two tenants.
    /// </summary>
    [Fact]
    public async Task A_partition_table_holding_the_unqualified_global_index_is_reported()
    {
        RequireBackingStore();
        var partition = NewPartition("legacy-named");
        var table = $"{NamedRoot}#{partition}";
        var direct = new SqliteConnectionStringBuilder(Fixture.ConnectionString) { Pooling = false }.ToString();
        try
        {
            ConformanceTenancy.Segmented = false;
            await using (await BootAsync())
            using (Lease(partition))
                await new ConformanceNamedMember { Code = "seed" }.Save();

            // The partition table as a build before DATA-0160 left it: the declared name itself, not a qualified copy.
            await using (var connection = new SqliteConnection(direct))
            {
                await connection.OpenAsync();
                await using var read = connection.CreateCommand();
                read.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = @table AND name LIKE @prefix";
                read.Parameters.AddWithValue("@table", table);
                read.Parameters.AddWithValue("@prefix", NamedIndex + "%");
                string qualified, sql;
                await using (var reader = await read.ExecuteReaderAsync())
                {
                    (await reader.ReadAsync()).Should().BeTrue("the global index was provisioned on the partition table");
                    qualified = reader.GetString(0);
                    sql = reader.GetString(1);
                }
                await using var legacy = connection.CreateCommand();
                legacy.CommandText = $"""DROP INDEX "{qualified}"; {sql.Replace($"\"{qualified}\"", $"\"{NamedIndex}\"")}""";
                await legacy.ExecuteNonQueryAsync();
            }

            ConformanceTenancy.Segmented = true;
            var logs = new CapturingLoggerProvider();
            await using var host = await BootAsync(services => services.AddSingleton<ILoggerProvider>(logs));
            using var _ = Lease(partition);
            using (ConformanceTenancy.Use("acme")) await new ConformanceNamedMember { Code = "a-1" }.Save();

            var report = logs.Warnings.Should().ContainSingle(entry =>
                    entry.Message.Contains("global unique index", StringComparison.Ordinal))
                .Which;
            Output.WriteLine(report.Message);
            report.State["Index"].Should().Be(NamedIndex);
            report.State["Drop"].Should().Be($"""DROP INDEX "{NamedIndex}";""");

            await DropReportedIndex(report.State);
            using (ConformanceTenancy.Use("globex")) await new ConformanceNamedMember { Code = "a-1" }.Save();
            using (ConformanceTenancy.Use("globex")) (await ConformanceNamedMember.All()).Should().ContainSingle();
        }
        finally
        {
            ConformanceTenancy.Segmented = false;
        }
    }
}
