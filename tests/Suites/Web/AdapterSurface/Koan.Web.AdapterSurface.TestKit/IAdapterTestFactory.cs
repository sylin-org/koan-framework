using Xunit;

namespace Koan.Web.AdapterSurface.TestKit;

/// <summary>
/// Adapter-specific factory contract consumed by <see cref="AdapterSurfaceSpecsBase{TFactory}"/>.
/// Each concrete test project implements this against its target adapter.
/// </summary>
public interface IAdapterTestFactory : IAsyncLifetime
{
    /// <summary>True when the backing infrastructure (Docker, local instance, env var) is reachable.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason when IsAvailable is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>Configured HTTP client pointed at the test host.</summary>
    HttpClient Client { get; }

    /// <summary>The DI provider of the running test host (so tests can call entity-first APIs).</summary>
    IServiceProvider Services { get; }

    /// <summary>Wipes the backing store. Called by tests between scenarios for isolation.</summary>
    Task ResetAsync();
}
