using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Isolation;

/// <summary>
/// ARCH-0103 P2 (the `KeyValueStore` family base) — InMemory now realizes <b>Shared</b> mode: it announces
/// <c>DataCaps.Isolation.RowScoped</c>, write-stamps the framework-managed discriminator into its object-graph sidecar,
/// guards a cross-scope write, and evaluates the managed read-filter via the hybrid evaluator. Proven through a real
/// <c>AddKoan()</c> boot by the cross-adapter <see cref="ManagedFieldNoLeak"/> oracle (read isolation · get-by-id IDOR ·
/// cross-scope write-reject · scoped RemoveAll) — the SAME matrix the relational reference passes.
///
/// <para>Before P2 InMemory did not declare <c>RowScoped</c>, so a managed-scoped entity routed to it failed closed at
/// the facade gate; this spec was RED and is now GREEN. Container mode (per ambient partition) is proven separately by
/// <c>InMemoryPartitionSpec</c>; Database mode (per routed source) by <c>InMemoryDatabaseRoutingSpec</c>.</para>
/// </summary>
public sealed class InMemoryManagedFieldNoLeakSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Fact(DisplayName = "InMemory: a managed scope isolates reads · IDOR · cross-scope write · RemoveAll (Shared mode)")]
    public async Task InMemory_realizes_shared_mode()
    {
        RequireBackingStore();
        await using var host = await BootAsync();

        // The generic, tenancy-independent managed-field oracle: registers __scope, runs the full no-leak matrix against
        // whatever adapter the ambient host resolves (here InMemory), and resets the registry on exit.
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
