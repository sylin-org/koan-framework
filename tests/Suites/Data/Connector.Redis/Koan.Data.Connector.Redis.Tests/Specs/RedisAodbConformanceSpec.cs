using System.Collections.Generic;
using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Redis.Tests.Specs;

/// <summary>
/// Redis's AODB conformance ledger cell. Proves the greenfield adapter realizes all three AODB
/// modes and declares the matching tokens. The two routed conformance sources share one Redis connection
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
