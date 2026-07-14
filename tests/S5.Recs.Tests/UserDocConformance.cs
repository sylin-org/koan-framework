using System;
using System.Collections.Generic;
using Koan.Testing;
using S5.Recs.Models;
using Xunit;

// Conformance hosts select the process-default ambient provider — run them sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace S5.Recs.Tests;

/// <summary>
/// P2.1 dogfood — the S5.Recs <see cref="UserDoc"/> entity inherits a test suite. UserDoc has no forced
/// adapter, so the conformance run pins it to the in-memory adapter (S5.Recs's real adapter is Mongo,
/// which would need a container) — proving the kit works on a real multi-provider sample entity without
/// Docker. Round-trip, pushdown-vs-oracle, paging and partition isolation all run.
/// </summary>
public sealed class UserDocConformance : EntityConformanceSpecs<UserDoc>
{
    protected override UserDoc NewValid()
        => new() { Name = "Ada Lovelace", IsDefault = false, CreatedAt = DateTimeOffset.UtcNow };

    protected override void Configure(IDictionary<string, string?> settings)
        => settings["Koan:Data:Sources:Default:Adapter"] = "inmemory";
}
