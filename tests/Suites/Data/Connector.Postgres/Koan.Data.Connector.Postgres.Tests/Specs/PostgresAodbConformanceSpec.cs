using System;
using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;
using Npgsql;

namespace Koan.Data.Connector.Postgres.Tests.Specs;

/// <summary>
/// PostgreSQL's AODB conformance ledger cell (ARCH-0103 §6 / P5). Proves the relational realization of all three AODB
/// modes on Postgres AND declares the tokens. For the Database cell the two routed conformance sources point to
/// freshly-created physical databases on the fixture's server (Database mode = a distinct physical database per source).
/// </summary>
public sealed class PostgresAodbConformanceSpec(PostgresFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<PostgresFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "postgres",
        ["Koan:Data:Sources:conformance_a:ConnectionString"] = ProvisionDatabase("a"),
        ["Koan:Data:Sources:conformance_b:Adapter"] = "postgres",
        ["Koan:Data:Sources:conformance_b:ConnectionString"] = ProvisionDatabase("b"),
    };

    /// <summary>Create a fresh physical database on the fixture's server and return a connection string targeting it.
    /// A unique per-run name keeps each Database-cell run clean without DROP churn.</summary>
    private string ProvisionDatabase(string slot)
    {
        var dbName = "koan_aodb_conf_" + slot + "_" + Guid.CreateVersion7().ToString("n")[..12];
        using (var admin = new NpgsqlConnection(Fixture.ConnectionString))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";   // CREATE DATABASE cannot run inside a transaction.
            cmd.ExecuteNonQuery();
        }
        return new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = dbName }.ConnectionString;
    }
}
