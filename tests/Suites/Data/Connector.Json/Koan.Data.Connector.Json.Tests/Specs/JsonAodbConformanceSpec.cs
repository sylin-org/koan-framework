using System;
using System.Collections.Generic;
using System.IO;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Json.Tests.Specs;

/// <summary>
/// JSON's AODB conformance ledger cell (ARCH-0103 §6 / P5). Proves the KeyValueStore JSON-text family realizes all
/// three AODB modes AND declares the tokens. The two routed conformance sources resolve to distinct on-disk
/// directory trees (per-source <c>DirectoryPath</c>).
/// </summary>
public sealed class JsonAodbConformanceSpec(JsonFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<JsonFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "json",
        ["Koan:Data:Sources:conformance_a:json:DirectoryPath"] = ConformanceDir("a"),
        ["Koan:Data:Sources:conformance_b:Adapter"] = "json",
        ["Koan:Data:Sources:conformance_b:json:DirectoryPath"] = ConformanceDir("b"),
    };

    private static string ConformanceDir(string slot)
    {
        var dir = Path.Combine(Path.GetTempPath(), "koan-aodb-conf", Guid.CreateVersion7().ToString("n") + "-" + slot);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
