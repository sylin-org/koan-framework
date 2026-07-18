using System.Collections.Generic;
using System.Threading.Tasks;
using Testcontainers.Redis;

namespace Koan.Testing.Containers;

/// <summary>
/// ARCH-0091 Redis container fixture. Starts the official <see cref="RedisContainer"/> module
/// (<c>GetConnectionString()</c> returns the StackExchange.Redis <c>host:port</c> form) and hands it to
/// the Koan data layer. A dedicated container per assembly makes the old per-execution db-index isolation
/// unnecessary — specs isolate via per-test partitions (key-prefix) on db 0. The TTL spec reads
/// <see cref="Database"/> + the bound <c>IConnectionMultiplexer</c> to assert native key expiry directly.
/// </summary>
public sealed class RedisFixture : KoanContainerFixture
{
    private RedisContainer? _container;

    public override string Engine => "redis";
    protected override string Adapter => "redis";

    /// <summary>The Redis logical database index every spec targets (0 — a dedicated container per assembly).</summary>
    public int Database => 0;

    protected override async Task<string> StartContainerAsync()
    {
        _container = new RedisBuilder("redis:7-alpine").Build();
        await _container.StartAsync().ConfigureAwait(false);
        return _container.GetConnectionString();
    }

    protected override ValueTask StopContainerAsync()
        => _container is null ? ValueTask.CompletedTask : _container.DisposeAsync();

    protected override IEnumerable<KeyValuePair<string, string?>> ExtraSettings(string connectionString) => new[]
    {
        new KeyValuePair<string, string?>("ConnectionStrings:Redis", connectionString),
    };
}
