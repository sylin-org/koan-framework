using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Instructions;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Tests.Specs;

/// <summary>
/// A generated column can go stale without its type changing, and nothing about its shape says so.
///
/// <para>Fixing the JSON-null read (PMC-038) changed the expression these columns are built from. New tables get
/// the new expression; a table an earlier Koan created keeps the old one, so on an existing database the
/// null-write defect survives the upgrade <i>and</i> the optimizer stops substituting the column, which silently
/// retires every index built on it. Neither is visible in the column's type or nullability, which is all this
/// store used to compare (PMC-045).</para>
/// </summary>
public sealed class MySqlProjectionDriftSpec(MySqlFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<MySqlFixture>(fixture, output)
{
    [Fact(DisplayName = "MySQL: a generated column Koan wrote validates clean")]
    public async Task A_current_projection_validates_clean()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        await new DriftProbe { Id = "fresh", Rank = 1 }.Save();

        var report = await Report(host);
        Output.WriteLine(string.Join(" | ", (string[])report["Findings"]!));
        report["State"].Should().Be("Healthy", "Koan wrote this column and its recipe is the current one");
    }

    [Fact(DisplayName = "MySQL: a generated column built by an older Koan is reported, not ignored")]
    public async Task A_stale_projection_is_reported()
    {
        RequireBackingStore();
        string table;
        await using (var host = await BootAsync())
        {
            await new DriftProbe { Id = "seed", Rank = 1 }.Save();
            table = (string)(await Report(host))["Table"]!;
        }

        // Exactly what an upgraded database holds: the expression an earlier Koan generated, before the JSON
        // null read was fixed, and no recipe marker because that Koan did not know to leave one.
        await using (var connection = new MySqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var alter = connection.CreateCommand();
            alter.CommandText =
                $"ALTER TABLE `{table}` MODIFY COLUMN `Rank` int " +
                "GENERATED ALWAYS AS (CAST(JSON_UNQUOTE(JSON_EXTRACT(`Json`, '$.\"Rank\"')) AS SIGNED)) STORED";
            await alter.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        // A second host so the readiness cache is not answering for the shape as it was.
        await using var upgraded = await BootAsync();
        var report = await Report(upgraded);
        var findings = (string[])report["Findings"]!;

        Output.WriteLine(string.Join(" | ", findings));
        findings.Should().Contain(finding => finding.Contains("Rank", StringComparison.Ordinal),
            "the column that needs rebuilding has to be named");
        report["State"].Should().Be("Degraded",
            "a stale projection still answers reads, so it is reported rather than made fatal");
    }

    private static async Task<IReadOnlyDictionary<string, object?>> Report(BoundHost host) =>
        await host.Services.GetRequiredService<IDataService>()
            .Execute<DriftProbe, string, IReadOnlyDictionary<string, object?>>(
                new Instruction(RelationalInstructions.SchemaValidate));

    [Storage(Name = "KOAN_PROJECTION_DRIFT_PROBE")]
    private sealed class DriftProbe : Entity<DriftProbe>
    {
        public int Rank { get; set; }
    }
}
