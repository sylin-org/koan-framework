using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonRuntimeExtensionsSpec
{
    [Fact]
    public async Task Instance_surface_reports_a_typed_missing_runtime()
    {
        await using var incomplete = new ServiceCollection().BuildServiceProvider();
        using var hostScope = AppHost.PushScope(incomplete);

        var act = () => new ExtensionCanon().Canonize();

        var error = (await act.Should().ThrowAsync<KoanHostContextException>()).Which;
        error.Failure.Should().Be(KoanHostContextException.FailureKind.MissingService);
        error.RequiredService.Should().Be(typeof(ICanonRuntime));
        error.Operation.Should().Be("entity canonization");
    }

    [Fact]
    public async Task Explicit_provider_overloads_select_and_restore_their_host()
    {
        await using var outer = new ServiceCollection().BuildServiceProvider();
        var runtime = new CapturingRuntime();
        await using var selected = new ServiceCollection()
            .AddSingleton<ICanonRuntime>(runtime)
            .BuildServiceProvider();
        var entity = new ExtensionCanon { Id = "selected" };

        using (AppHost.PushScope(outer))
        {
            await entity.Canonize(selected);
            runtime.CanonizeProvider.Should().BeSameAs(selected);
            AppHost.Current.Should().BeSameAs(outer);

            await entity.RebuildViews(selected, ["canonical"]);
            runtime.RebuildProvider.Should().BeSameAs(selected);
            AppHost.Current.Should().BeSameAs(outer);
        }
    }

    private sealed class CapturingRuntime : ICanonRuntime
    {
        public IServiceProvider? CanonizeProvider { get; private set; }
        public IServiceProvider? RebuildProvider { get; private set; }

        public async Task<CanonizationResult<T>> Canonize<T>(
            T entity,
            CanonizationOptions? options = null,
            CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
        {
            await Task.Yield();
            CanonizeProvider = AppHost.Current;
            return new CanonizationResult<T>(
                entity,
                CanonizationOutcome.Canonized,
                entity.Metadata.Clone());
        }

        public async Task RebuildViews<T>(
            string canonicalId,
            string[]? views = null,
            CancellationToken cancellationToken = default)
            where T : CanonEntity<T>, new()
        {
            await Task.Yield();
            RebuildProvider = AppHost.Current;
        }

    }

    private sealed class ExtensionCanon : CanonEntity<ExtensionCanon>;

}
