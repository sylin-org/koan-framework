using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.InMemory.Tests.Specs;

/// <summary>
/// InMemory's AODB conformance ledger cell (ARCH-0103 §6 / P5) — the byte-faithful convergence oracle. Proves the
/// KeyValueStore family base realizes all three AODB modes on InMemory AND declares the tokens. The two routed
/// conformance sources isolate purely by source key (the in-memory store keys by source/type/partition).
/// </summary>
public sealed class InMemoryAodbConformanceSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<InMemoryFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "inmemory",
        ["Koan:Data:Sources:conformance_b:Adapter"] = "inmemory",
    };
}
