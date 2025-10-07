using Koan.Testing.Contracts;
using Koan.Testing.Diagnostics;
using Koan.Testing.Fixtures;
using Xunit.Abstractions;

namespace Koan.Testing.Pipeline;

public sealed class TestPipeline
{
    private readonly string _suite;
    private readonly string _scenario;
    private readonly ITestOutputHelper _output;
    private readonly CancellationToken _cancellation;
    private readonly List<Func<TestContext, ValueTask>> _arrangeSteps = new();
    private Func<TestContext, ValueTask>? _actStep;
    private Func<TestContext, ValueTask>? _assertStep;
    private readonly List<FixtureBinding> _fixtures = new();

    private TestPipeline(string suite, string scenario, ITestOutputHelper output, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suite);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        _suite = suite;
        _scenario = scenario;
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _cancellation = cancellation;
    }

    public static TestPipeline For<TSuite>(ITestOutputHelper output, string scenario, CancellationToken cancellation = default)
    {
        var suite = typeof(TSuite).Assembly.GetName().Name ?? typeof(TSuite).Namespace ?? typeof(TSuite).Name;
        return new TestPipeline(suite, scenario, output, cancellation);
    }

    public static TestPipeline ForSuite(string suiteName, ITestOutputHelper output, string scenario, CancellationToken cancellation = default)
        => new(suiteName, scenario, output, cancellation);

    public TestPipeline Arrange(Func<TestContext, ValueTask> step)
    {
        _arrangeSteps.Add(step ?? throw new ArgumentNullException(nameof(step)));
        return this;
    }

    public TestPipeline Arrange(Action<TestContext> step)
        => Arrange(ctx =>
        {
            step(ctx);
            return ValueTask.CompletedTask;
        });

    public TestPipeline Act(Func<TestContext, ValueTask> step)
    {
        _actStep = step ?? throw new ArgumentNullException(nameof(step));
        return this;
    }

    public TestPipeline Act(Action<TestContext> step)
        => Act(ctx =>
        {
            step(ctx);
            return ValueTask.CompletedTask;
        });

    public TestPipeline Assert(Func<TestContext, ValueTask> step)
    {
        _assertStep = step ?? throw new ArgumentNullException(nameof(step));
        return this;
    }

    public TestPipeline Assert(Action<TestContext> step)
        => Assert(ctx =>
        {
            step(ctx);
            return ValueTask.CompletedTask;
        });

    public TestPipeline Using<TFixture>(string key, Func<TestContext, ValueTask<TFixture>> factory, Action<TestContext, TFixture>? onReady = null)
        where TFixture : class, IAsyncDisposable
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _fixtures.Add(new FixtureBinding(
            key,
            typeof(TFixture),
            async ctx =>
            {
                var fixture = await factory(ctx).ConfigureAwait(false);
                if (fixture is IInitializableFixture initializable)
                {
                    await initializable.InitializeAsync(ctx).ConfigureAwait(false);
                }

                onReady?.Invoke(ctx, fixture);
                return fixture;
            }));
        return this;
    }

    public async Task RunAsync()
    {
        var diagnostics = new TestDiagnostics(_output, _suite, _scenario);
        using var scope = diagnostics.BeginScope("pipeline", new { suite = _suite, scenario = _scenario });
        var context = new TestContext(_suite, _scenario, diagnostics, _cancellation);
        var handles = new Stack<TestFixtureHandle>();

        try
        {
            foreach (var binding in _fixtures)
            {
                diagnostics.Debug("fixture.allocate", new { binding.Key, binding.FixtureType.FullName });
                var fixture = await binding.Factory(context).ConfigureAwait(false);
                context.SetItem(binding.Key, fixture);
                handles.Push(new AnonymousHandle(binding.Key, fixture, diagnostics));
            }

            foreach (var arrange in _arrangeSteps)
            {
                diagnostics.Debug("step.arrange", new { scenario = _scenario });
                await arrange(context).ConfigureAwait(false);
            }

            if (_actStep is not null)
            {
                diagnostics.Debug("step.act", new { scenario = _scenario });
                await _actStep(context).ConfigureAwait(false);
            }

            if (_assertStep is not null)
            {
                diagnostics.Debug("step.assert", new { scenario = _scenario });
                await _assertStep(context).ConfigureAwait(false);
            }
        }
        finally
        {
            while (handles.Count > 0)
            {
                var handle = handles.Pop();
                await handle.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed record FixtureBinding(string Key, Type FixtureType, Func<TestContext, ValueTask<IAsyncDisposable>> Factory);

    private sealed class AnonymousHandle : TestFixtureHandle
    {
        private readonly IAsyncDisposable _inner;

        public AnonymousHandle(string name, IAsyncDisposable inner, ITestDiagnostics diagnostics)
            : base(name, diagnostics)
        {
            _inner = inner;
        }

        public override ValueTask DisposeAsync() => DisposeCoreAsync(() => _inner.DisposeAsync());
    }
}
