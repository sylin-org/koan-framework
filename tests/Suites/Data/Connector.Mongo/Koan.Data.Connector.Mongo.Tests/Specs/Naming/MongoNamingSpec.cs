using System.Text;
using AwesomeAssertions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Mongo;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.Connector.Mongo.Tests.Specs.Naming;

/// <summary>
/// Mongo collection-name generation: the adapter must declare a namespace-safe identifier limit so an
/// over-long partition suffix is hashed down (injectively) rather than producing an invalid collection name.
/// </summary>
public sealed class MongoNamingSpec
{
    private sealed class Doc : Entity<Doc>
    {
        public string Name { get; set; } = "";
    }

    private static StorageNamingCapability Capability()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<MongoOptions>(_ => { });
        using var sp = services.BuildServiceProvider();
        return new MongoAdapterFactory().GetNamingCapability(sp);
    }

    [Fact]
    public void Capability_declares_a_namespace_safe_identifier_limit()
    {
        // 255-byte namespace (db.collection) budget, reserving the max 64-byte db name + '.'.
        Capability().MaxIdentifierBytes.Should().Be(255 - 64 - 1);
    }

    [Fact]
    public void Over_long_partition_name_is_clamped_injectively()
    {
        var cap = Capability();
        var limit = cap.MaxIdentifierBytes!.Value;

        var name = StorageNameGenerator.Generate(typeof(Doc), new string('a', 300), cap);
        Encoding.UTF8.GetByteCount(name).Should().BeLessThanOrEqualTo(limit,
            "an over-limit partition must be hashed down, not produce an invalid Mongo collection name");

        // Distinct long partitions must stay distinct — the clamp hashes, it does not truncate-and-collide.
        var other = StorageNameGenerator.Generate(typeof(Doc), new string('a', 299) + "b", cap);
        name.Should().NotBe(other);
    }
}
