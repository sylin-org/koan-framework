using System;
using System.Linq;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Capabilities;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Koan.Data.Connector.Redis.Tests.Specs.Retention;

/// <summary>
/// (DATA-0101) Native TTL for the Redis data connector: a single-property <c>[Index(Ttl = true)]</c>
/// timestamp must (a) be self-reported via <see cref="DataCaps.Retention"/>.TtlIndex, (b) set a real
/// store-native key expiry (EXPIREAT) on write for a future instant, and (c) leave the key persistent
/// when the TTL value is null. Runs against a real Redis container; key expiry is read directly via the
/// connection multiplexer (the contract is the Redis key's TTL, not an entity round-trip).
/// </summary>
public sealed class RedisTtlSpec(RedisFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<RedisFixture>(fixture, output)
{
    [Fact]
    public async Task Ttl_index_declares_capability_and_sets_native_key_expiry()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        var partition = NewPartition("ttl");
        using var lease = Lease(partition);

        // (a) Self-reports native TTL.
        var repository = data.GetRepository<TtlProbe, string>();
        var caps = DataCaps.Describe(repository, repository.GetType().Name);
        caps.Has(DataCaps.Retention.TtlIndex).Should().BeTrue();

        // (b) A future [Index(Ttl)] instant sets a real key expiry.
        var expiring = await TtlProbe.Upsert(new TtlProbe
        {
            Name = "expiring",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        });

        // (c) A null TTL value persists (no expiry).
        var persistent = await TtlProbe.Upsert(new TtlProbe
        {
            Name = "persistent",
            ExpiresAt = null
        });

        var muxer = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = muxer.GetDatabase(Fixture.Database);

        var expiringTtl = await db.KeyTimeToLiveAsync(KeyFor(muxer, Fixture.Database, expiring.Id));
        expiringTtl.Should().NotBeNull("a future [Index(Ttl)] instant must set the key's expiry");
        expiringTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(4));
        expiringTtl!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));

        var persistentTtl = await db.KeyTimeToLiveAsync(KeyFor(muxer, Fixture.Database, persistent.Id));
        persistentTtl.Should().BeNull("a null TTL value must leave the key persistent");
    }

    [Fact]
    public async Task Ttl_index_applies_per_key_expiry_on_batch_upsert()
    {
        RequireBackingStore();
        await using var host = await BootAsync();
        var data = host.Services.GetRequiredService<IDataService>();
        var partition = NewPartition("ttl-batch");
        using var lease = Lease(partition);

        // UpsertMany (the batch / RedisBatch.Save path) must apply TTL per key, independently.
        var repository = data.GetRepository<TtlProbe, string>();
        var future = new TtlProbe { Id = Guid.CreateVersion7().ToString(), Name = "b-future", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5) };
        var persistent = new TtlProbe { Id = Guid.CreateVersion7().ToString(), Name = "b-null", ExpiresAt = null };

        var written = await repository.UpsertMany(new[] { future, persistent });
        written.Should().Be(2); // both upserted (TTL expiry is orthogonal to the upserted count — matches Mongo)

        var muxer = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = muxer.GetDatabase(Fixture.Database);

        var futureTtl = await db.KeyTimeToLiveAsync(KeyFor(muxer, Fixture.Database, future.Id));
        futureTtl.Should().NotBeNull("a future [Index(Ttl)] instant must set the key's expiry on batch upsert");
        futureTtl!.Value.Should().BeGreaterThan(TimeSpan.FromMinutes(4));
        futureTtl!.Value.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));

        var persistentTtl = await db.KeyTimeToLiveAsync(KeyFor(muxer, Fixture.Database, persistent.Id));
        persistentTtl.Should().BeNull("a null TTL value must leave the key persistent on batch upsert");
    }

    // Resolve the Redis key for an entity id without recomputing the keyspace: the key is "{keyspace}:{id}".
    private static RedisKey KeyFor(IConnectionMultiplexer muxer, int database, string id)
    {
        var server = muxer.GetServers().First(s => s.IsConnected);
        return server.Keys(database, pattern: $"*:{id}").Single();
    }

    private sealed class TtlProbe : Entity<TtlProbe>
    {
        public string Name { get; set; } = "";

        [Index(Ttl = true)]
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
