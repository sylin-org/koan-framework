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

    [Fact(DisplayName = "MySQL: a generated column built by an older Koan is rebuilt on the boot that notices")]
    public async Task A_stale_projection_is_rebuilt()
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

        (await Comment(table)).Should().BeEmpty("this is the column an upgraded database actually holds");

        // A second host so the readiness cache is not answering for the shape as it was. Booting provisions,
        // and provisioning is where a projection that drifted is rebuilt.
        await using var upgraded = await BootAsync();
        var report = await Report(upgraded);

        Output.WriteLine(string.Join(" | ", (string[])report["Findings"]!));
        report["State"].Should().Be("Healthy", "the boot that noticed the stale column is the boot that fixed it");

        // Asserting the report alone would pass if validation had merely stopped looking. The recipe marker is
        // written by the ALTER and by nothing else, so its presence is the statement having run.
        (await Comment(table)).Should().StartWith("koan-gen:",
            "the rebuilt column carries the recipe it was built from");

        // The rebuilt column computes the current expression, so a null round-trips (PMC-038) where the old one
        // wrote the string "null".
        await new DriftProbe { Id = "after", Rank = 7 }.Save();
        (await DriftProbe.Get("after"))!.Rank.Should().Be(7);
    }

    private async Task<string> Comment(string table)
    {
        await using var connection = new MySqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var read = connection.CreateCommand();
        read.CommandText =
            "SELECT column_comment FROM information_schema.columns " +
            "WHERE table_schema = DATABASE() AND table_name = @table AND column_name = 'Rank'";
        read.Parameters.AddWithValue("table", table);
        return (string?)await read.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? string.Empty;
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
