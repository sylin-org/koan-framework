using Koan.Data.AdapterSurface.TestKit;

namespace Koan.Data.Connector.Redis.Tests.Specs.Isolation;

/// <summary>
/// Redis realizes <b>Shared</b> mode directly: it
/// announces <c>DataCaps.Isolation.RowScoped</c>, write-stamps the framework-managed discriminator into the stored JSON
/// value via the shared <c>ManagedFieldJsonInjector</c>, extracts it on read, guards cross-scope writes, and evaluates
/// managed filters inside its bounded logical set. Proven through a real <c>AddKoan()</c> boot by the cross-adapter
/// <see cref="ManagedFieldNoLeak"/> oracle (read isolation · get-by-id IDOR · cross-scope write-reject · scoped
/// RemoveAll) used by the other conformant adapters.
/// </summary>
public sealed class RedisManagedFieldNoLeakSpec(RedisFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<RedisFixture>(fixture, output)
{
    [Fact(DisplayName = "Redis: a managed scope isolates reads · IDOR · cross-scope write · RemoveAll (Shared mode)")]
    public async Task Redis_realizes_shared_mode()
    {
        RequireBackingStore();
        await using var host = await BootAsync(ManagedFieldNoLeak.Declare);

        // The generic, tenancy-independent managed-field oracle: registers __scope, runs the full no-leak matrix against
        // whatever adapter the ambient host resolves (here Redis), and resets the registry on exit.
        await ManagedFieldNoLeak.AssertNoLeakAsync();
    }
}
