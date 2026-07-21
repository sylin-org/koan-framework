using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// Selects the shared Identity fixture host for one xUnit test flow.
/// </summary>
/// <remarks>
/// The fixture owns host and store lifetime; this base owns only ambient selection. A fact may start
/// and stop another Koan host without making later facts depend on process-default restoration.
/// </remarks>
public abstract class IdentityHostScopedSpec(IdentityHostFixture fixture) : IAsyncLifetime
{
    private IDisposable? _hostScope;

    public ValueTask InitializeAsync()
    {
        _hostScope = AppHost.PushScope(fixture.Services);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _hostScope?.Dispose();
        _hostScope = null;
        return ValueTask.CompletedTask;
    }
}
