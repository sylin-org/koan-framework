using System;
using System.Collections.Generic;
using System.IO;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.DuckDb.Tests.Specs;

/// <summary>
/// DuckDB's AODB conformance suite (ARCH-0103 §6 / P5) is the relational reference (P0, already conformant; its
/// specs seed the kit). Proves the relational realization of all three AODB modes AND declares the tokens. The two
/// routed conformance sources resolve to distinct on-disk database files (per-source connection string).
/// </summary>
public sealed class DuckDbAodbConformanceSpec(DuckDbFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<DuckDbFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "duckdb",
        ["Koan:Data:Sources:conformance_a:ConnectionString"] = ConformanceFile("a"),
        ["Koan:Data:Sources:conformance_b:Adapter"] = "duckdb",
        ["Koan:Data:Sources:conformance_b:ConnectionString"] = ConformanceFile("b"),
    };

    private static string ConformanceFile(string slot)
    {
        var dir = Path.Combine(Path.GetTempPath(), "koan-aodb-conf");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Guid.CreateVersion7().ToString("n") + "-" + slot + ".db");
        return $"Data Source={file}";
    }
}
