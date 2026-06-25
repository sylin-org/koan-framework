using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Json.Tests.Specs.Isolation;

/// <summary>
/// ARCH-0103 P2 (the `KeyValueStore` family base, the JSON-text family) — Json now realizes <b>Shared</b> mode: it
/// announces <c>DataCaps.Isolation.RowScoped</c>, write-stamps the framework-managed discriminator into the persisted
/// JSON via the shared <c>ManagedFieldJsonInjector</c> (the same write-stamp the relational trio uses), extracts it back
/// on read, guards a cross-scope write, and evaluates the managed read-filter via the base's hybrid evaluator. Proven
/// through a real <c>AddKoan()</c> boot by the cross-adapter <see cref="ManagedFieldNoLeak"/> oracle (read isolation ·
/// get-by-id IDOR · cross-scope write-reject · scoped RemoveAll) — the SAME matrix the relational reference and InMemory
/// pass.
///
/// <para>Before P2 Json did not declare <c>RowScoped</c> and serialized the raw POCO (losing the managed field), so a
/// managed-scoped entity routed to it failed closed at the facade gate; this spec was RED and is now GREEN. Container
/// mode (a JSON file per ambient partition) is proven separately by <c>JsonPartitionSpec</c>; Database mode (a directory
/// per routed source) by <c>JsonDatabaseRoutingSpec</c>.</para>
/// </summary>
public sealed class JsonManagedFieldNoLeakSpec(JsonFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<JsonFixture>(fixture, output)
{
    [Fact(DisplayName = "Json: a managed scope isolates reads · IDOR · cross-scope write · RemoveAll (Shared mode)")]
    public async Task Json_realizes_shared_mode()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // The generic, tenancy-independent managed-field oracle: registers __scope, runs the full no-leak matrix against
        // whatever adapter the ambient host resolves (here Json), and resets the registry on exit.
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
