using Koan.Testing.Containers;

namespace Koan.Web.AdapterSurface.TestKit.Containers;

/// <summary>
/// Bridges the Web adapter matrix's reset needs to the public Koan container fixtures. Container
/// construction, endpoint override, readiness, and owned teardown remain in one package owner.
/// </summary>
public abstract class KoanWebContainerHelper<TFixture> : IAsyncDisposable
    where TFixture : KoanContainerFixture, new()
{
    protected TFixture Fixture { get; } = new();

    public bool IsAvailable => Fixture.IsAvailable;
    public string? UnavailableReason => Fixture.Reason;
    public virtual string? ConnectionString => Fixture.ConnectionString;

    public virtual async Task InitializeAsync() => await Fixture.InitializeAsync().ConfigureAwait(false);

    public virtual ValueTask DisposeAsync() => Fixture.DisposeAsync();
}
