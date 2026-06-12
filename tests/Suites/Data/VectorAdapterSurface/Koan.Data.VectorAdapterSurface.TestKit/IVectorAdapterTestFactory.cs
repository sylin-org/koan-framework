using Xunit;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// Adapter-specific factory contract consumed by <see cref="VectorAdapterSurfaceSpecsBase{TFactory}"/>
/// and its siblings. Each concrete test project implements this against its target vector adapter.
/// </summary>
/// <remarks>
/// Mirrors <c>IAdapterTestFactory</c> from the data matrix. The factory owns the lifecycle of the
/// real (or fake) backing store and provides the <see cref="IServiceProvider"/> the spec bases use
/// to drive <c>Vector&lt;T&gt;</c> calls through <c>AppHost.PushScope</c>.
/// </remarks>
public interface IVectorAdapterTestFactory : IAsyncLifetime, IVectorAdapterCapabilities
{
    /// <summary>True when the backing infrastructure (Docker, local instance, env var) is reachable.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason when IsAvailable is false.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// The DI provider configured with the target vector adapter registered as
    /// <see cref="Koan.Data.Vector.Abstractions.IVectorAdapterFactory"/>. Spec bases push this
    /// onto <c>AppHost.Current</c> via <c>AppHost.PushScope</c> so <c>Vector&lt;T&gt;.*</c> calls
    /// resolve through the adapter under test.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Embedding dimension the adapter is configured to accept. The kit standardizes on a small
    /// dimension (typically 8 or 16) for fast deterministic tests; adapter-specific extras that
    /// exercise realistic embedding sizes live outside the matrix.
    /// </summary>
    int EmbeddingDimension { get; }

    /// <summary>
    /// Wipes the backing store for a single entity type. Called between scenarios so each spec
    /// runs against a known-empty baseline.
    /// </summary>
    Task ResetAsync(CancellationToken ct = default);
}
