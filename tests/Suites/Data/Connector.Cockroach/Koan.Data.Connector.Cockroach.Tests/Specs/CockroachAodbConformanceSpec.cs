using System;
using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;
using Npgsql;

namespace Koan.Data.Connector.Cockroach.Tests.Specs;

/// <summary>
/// CockroachDB's AODB conformance ledger cell (ARCH-0094 Forge dogfood / ARCH-0103 §6). Proves the relational
/// realization of all three AODB modes on CockroachDB AND declares the tokens — reusing the shipped Postgres
/// connector's repository/dialect/DDL unchanged (CockroachDB ≈ Postgres over pg-wire). For the Database cell the two
/// routed conformance sources point to freshly-created physical databases on the fixture's node (Database mode = a
/// distinct physical database per source).
/// </summary>
public sealed class CockroachAodbConformanceSpec(CockroachFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<CockroachFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "cockroach",
        ["Koan:Data:Sources:conformance_a:ConnectionString"] = ProvisionDatabase("a"),
        ["Koan:Data:Sources:conformance_b:Adapter"] = "cockroach",
        ["Koan:Data:Sources:conformance_b:ConnectionString"] = ProvisionDatabase("b"),
    };

    /// <summary>Create a fresh physical database on the fixture's node and return a connection string targeting it.
    /// A unique per-run name keeps each Database-cell run clean without DROP churn. The provisioned databases are NOT
    /// dropped: under the Testcontainers fixture the whole node is reclaimed on teardown, and the GUID-suffixed names
    /// never collide — so a persistent bring-your-own-node (env-override) lane accumulates harmless orphan databases
    /// rather than risking a flaky mid-suite DROP against in-use connections.</summary>
    private string ProvisionDatabase(string slot)
    {
        var dbName = "koan_aodb_conf_" + slot + "_" + Guid.CreateVersion7().ToString("n")[..12];
        using (var admin = new NpgsqlConnection(Fixture.ConnectionString))
        {
            admin.Open();
            using var cmd = admin.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";   // CockroachDB auto-commits DDL; runs outside an explicit tx.
            cmd.ExecuteNonQuery();
        }
        return new NpgsqlConnectionStringBuilder(Fixture.ConnectionString) { Database = dbName }.ConnectionString;
    }
}
