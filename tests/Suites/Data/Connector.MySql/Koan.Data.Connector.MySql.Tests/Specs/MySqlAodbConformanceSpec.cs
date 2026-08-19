using Koan.Data.AdapterSurface.TestKit;
using MySqlConnector;

namespace Koan.Data.Connector.MySql.Tests.Specs;

public sealed class MySqlAodbConformanceSpec(MySqlFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<MySqlFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() =>
        new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:conformance_a:Adapter"] = "mysql",
            ["Koan:Data:Sources:conformance_a:ConnectionString"] = ProvisionDatabase("a"),
            ["Koan:Data:Sources:conformance_b:Adapter"] = "mysql",
            ["Koan:Data:Sources:conformance_b:ConnectionString"] = ProvisionDatabase("b"),
        };

    private string ProvisionDatabase(string slot)
    {
        var database = $"koan_aodb_conf_{slot}_{Guid.CreateVersion7():N}"[..31];
        var adminConnectionString = new MySqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = "mysql",
        }.ConnectionString;

        using var admin = new MySqlConnection(adminConnectionString);
        admin.Open();

        using var command = admin.CreateCommand();
        command.CommandText = $"CREATE DATABASE `{database}`";
        command.ExecuteNonQuery();

        return new MySqlConnectionStringBuilder(Fixture.ConnectionString)
        {
            Database = database,
        }.ConnectionString;
    }
}
