using System;
using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;
using Microsoft.Data.SqlClient;

namespace Koan.Data.Connector.SqlServer.Tests.Specs;

/// <summary>
/// SQL Server's AODB conformance ledger cell (ARCH-0103 §6 / P5). Proves the relational realization of all three AODB
/// modes on SQL Server AND declares the tokens. For the Database cell the two routed conformance sources point to
/// freshly-created physical databases on the fixture's server (Database mode = a distinct physical database per source).
/// </summary>
public sealed class SqlServerAodbConformanceSpec(SqlServerFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<SqlServerFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "sqlserver",
        ["Koan:Data:Sources:conformance_a:ConnectionString"] = ProvisionDatabase("a"),
        ["Koan:Data:Sources:conformance_b:Adapter"] = "sqlserver",
        ["Koan:Data:Sources:conformance_b:ConnectionString"] = ProvisionDatabase("b"),
    };

    /// <summary>Create a fresh physical database on the fixture's server and return a connection string targeting it.
    /// A unique per-run name keeps each Database-cell run clean without DROP churn. The provisioned databases are NOT
    /// dropped: under the Testcontainers fixture the whole server is reclaimed on teardown, and the GUID-suffixed names
    /// never collide — so a persistent bring-your-own-server (env-override) lane accumulates harmless orphan databases
    /// rather than risking a flaky mid-suite DROP against in-use connections.</summary>
    private string ProvisionDatabase(string slot)
    {
        var dbName = "koan_aodb_conf_" + slot + "_" + Guid.CreateVersion7().ToString("n")[..12];
        var adminCs = new SqlConnectionStringBuilder(Fixture.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        using (var admin = new SqlConnection(adminCs))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"IF DB_ID(N'{dbName}') IS NULL CREATE DATABASE [{dbName}]";
            cmd.ExecuteNonQuery();
        }
        return new SqlConnectionStringBuilder(Fixture.ConnectionString) { InitialCatalog = dbName }.ConnectionString;
    }
}
