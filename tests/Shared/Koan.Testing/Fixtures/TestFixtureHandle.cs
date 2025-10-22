using Koan.Testing.Diagnostics;

namespace Koan.Testing.Fixtures;

public abstract class TestFixtureHandle : IAsyncDisposable
{
    private bool _disposed;

    protected TestFixtureHandle(string name, ITestDiagnostics diagnostics)
    {
        Name = name;
        Diagnostics = diagnostics;
    }

    public string Name { get; }

    protected ITestDiagnostics Diagnostics { get; }

    public abstract ValueTask DisposeAsync();

    protected ValueTask DisposeCoreAsync(Func<ValueTask> dispose)
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        Diagnostics.Debug("fixture.disposed", new { fixture = Name });
        return dispose();
    }
}

public sealed class TestFixtureHandle<TFixture> : TestFixtureHandle where TFixture : IAsyncDisposable
{
    public TestFixtureHandle(string name, TFixture instance, ITestDiagnostics diagnostics)
        : base(name, diagnostics)
    {
        Instance = instance;
    }

    public TFixture Instance { get; }

    public override ValueTask DisposeAsync() => DisposeCoreAsync(() => Instance.DisposeAsync());
}
