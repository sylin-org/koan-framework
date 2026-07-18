using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Redis.Tests.Specs;

/// <summary>
/// Redis's AODB conformance ledger cell (ARCH-0103 §6 / P5). Proves the KeyValueStore family realizes all three AODB
/// modes on Redis AND declares the tokens. The two routed conformance sources share the one Redis connection
/// (<c>ConnectionStrings:Redis</c>, set by the fixture) but isolate by per-source logical-database index.
/// </summary>
public sealed class RedisAodbConformanceSpec(RedisFixture fixture, ITestOutputHelper output)
    : AodbConformanceSpecsBase<RedisFixture>(fixture, output)
{
    protected override IEnumerable<KeyValuePair<string, string?>> RoutedSourceSettings() => new Dictionary<string, string?>
    {
        ["Koan:Data:Sources:conformance_a:Adapter"] = "redis",
        ["Koan:Data:Sources:conformance_a:redis:Database"] = "1",
        ["Koan:Data:Sources:conformance_b:Adapter"] = "redis",
        ["Koan:Data:Sources:conformance_b:redis:Database"] = "2",
    };
}
