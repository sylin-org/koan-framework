using System;
using System.Threading;
using AwesomeAssertions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Model;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Naming;

/// <summary>
/// DATA-0105 phase 0a / ARCH-0096 §3: the base anchor is memoized at the (provider, entity) plane,
/// independent of partition. Resolving a NEW partition for an already-seen (provider, entity) must reuse the
/// cached anchor (not re-run the grammar / NameOverride), while the partition is still applied to the name.
/// </summary>
public class StorageAnchorCacheSpec
{
    public class Widget : Entity<Widget>
    {
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Anchor_is_resolved_once_and_reused_across_partitions()
    {
        var anchorCalls = 0;
        const string provider = "anchor-split-spec-A"; // unique provider isolates the static caches per test
        Func<StorageNamingCapability> cap = () => new StorageNamingCapability
        {
            NameOverride = _ => { Interlocked.Increment(ref anchorCalls); return "base"; },
        };

        var a = StorageNameGenerator.Resolve(provider, typeof(Widget), "alpha", cap);
        var b = StorageNameGenerator.Resolve(provider, typeof(Widget), "beta", cap);

        // Partition is still applied — the anchor cache must not freeze it.
        a.Should().Be("base#alpha");
        b.Should().Be("base#beta");
        // The anchor (NameOverride) was resolved ONCE for the (provider, entity) and reused for the new partition.
        anchorCalls.Should().Be(1);
    }

    [Fact]
    public void Repeated_same_partition_is_served_from_the_composed_cache()
    {
        var anchorCalls = 0;
        const string provider = "anchor-split-spec-B";
        Func<StorageNamingCapability> cap = () => new StorageNamingCapability
        {
            NameOverride = _ => { Interlocked.Increment(ref anchorCalls); return "base"; },
        };

        StorageNameGenerator.Resolve(provider, typeof(Widget), "alpha", cap).Should().Be("base#alpha");
        StorageNameGenerator.Resolve(provider, typeof(Widget), "alpha", cap).Should().Be("base#alpha");

        anchorCalls.Should().Be(1);
    }
}
