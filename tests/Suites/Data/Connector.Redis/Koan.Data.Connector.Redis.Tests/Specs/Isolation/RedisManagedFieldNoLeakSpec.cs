using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Redis.Tests.Specs.Isolation;

/// <summary>
/// ARCH-0103 P2 (the `KeyValueStore` family base, the JSON-text family) — Redis now realizes <b>Shared</b> mode: it
/// announces <c>DataCaps.Isolation.RowScoped</c>, write-stamps the framework-managed discriminator into the stored JSON
/// value via the shared <c>ManagedFieldJsonInjector</c> (the same write-stamp the relational trio and the Json adapter
/// use), extracts it back on read, guards a cross-scope write, and evaluates the managed read-filter via the base's
/// hybrid evaluator. Proven through a real <c>AddKoan()</c> boot against a live Redis container by the cross-adapter
/// <see cref="ManagedFieldNoLeak"/> oracle (read isolation · get-by-id IDOR · cross-scope write-reject · scoped
/// RemoveAll) — the SAME matrix the relational reference, InMemory, and Json pass.
///
/// <para>Before P2 Redis serialized the raw POCO (losing the managed field) and did not declare <c>RowScoped</c>, so a
/// managed-scoped entity routed to it failed closed at the facade gate. Container mode (a keyspace per ambient
/// partition) is exercised by the Web AdapterSurface Redis suite; Database mode (a Redis logical database per routed
/// source) by <c>RedisDatabaseRoutingSpec</c>.</para>
/// </summary>
public sealed class RedisManagedFieldNoLeakSpec(RedisFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<RedisFixture>(fixture, output)
{
    [Fact(DisplayName = "Redis: a managed scope isolates reads · IDOR · cross-scope write · RemoveAll (Shared mode)")]
    public async Task Redis_realizes_shared_mode()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // The generic, tenancy-independent managed-field oracle: registers __scope, runs the full no-leak matrix against
        // whatever adapter the ambient host resolves (here Redis), and resets the registry on exit.
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
