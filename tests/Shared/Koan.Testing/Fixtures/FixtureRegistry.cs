using Koan.Testing.Contracts;
using Koan.Testing.Diagnostics;

namespace Koan.Testing.Fixtures;

public sealed class FixtureRegistry
{
    private readonly Dictionary<string, FixtureRegistration> _registrations;

    public FixtureRegistry()
    {
        _registrations = new Dictionary<string, FixtureRegistration>(StringComparer.OrdinalIgnoreCase);
    }

    private FixtureRegistry(Dictionary<string, FixtureRegistration> registrations)
    {
        _registrations = new Dictionary<string, FixtureRegistration>(registrations, StringComparer.OrdinalIgnoreCase);
    }

    public FixtureRegistry Register<TFixture>(string name, Func<TestContext, ValueTask<TFixture>> factory)
        where TFixture : class, IAsyncDisposable
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _registrations[name] = new FixtureRegistration(name, typeof(TFixture), async (ctx, diagnostics) =>
        {
            var fixture = await factory(ctx).ConfigureAwait(false);
            if (fixture is IInitializableFixture initializable)
            {
                await initializable.InitializeAsync(ctx).ConfigureAwait(false);
            }

            diagnostics.Info("fixture.ready", new { fixture = name, type = typeof(TFixture).FullName });
            return fixture;
        });

        return this;
    }

    public FixtureRegistry Clone() => new(_registrations);

    public async ValueTask<TestFixtureHandle<TFixture>> ResolveAsync<TFixture>(string name, TestContext context, ITestDiagnostics diagnostics)
        where TFixture : class, IAsyncDisposable
    {
        if (!_registrations.TryGetValue(name, out var registration))
        {
            throw new InvalidOperationException($"Fixture '{name}' is not registered for suite '{context.Suite}'.");
        }

        if (!registration.FixtureType.IsAssignableTo(typeof(TFixture)))
        {
            throw new InvalidOperationException($"Fixture '{name}' is registered as '{registration.FixtureType.FullName}' but '{typeof(TFixture).FullName}' was requested.");
        }

        var instance = (TFixture)await registration.Factory(context, diagnostics).ConfigureAwait(false);
        return new TestFixtureHandle<TFixture>(name, instance, diagnostics);
    }

    private sealed record FixtureRegistration(
        string Name,
        Type FixtureType,
        Func<TestContext, ITestDiagnostics, ValueTask<IAsyncDisposable>> Factory
    );
}
