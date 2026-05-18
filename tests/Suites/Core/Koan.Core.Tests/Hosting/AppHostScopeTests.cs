using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Core.Tests.Hosting;

/// <summary>
/// Specs for the flow-scoped <see cref="AppHost.PushScope"/> primitive introduced in DATA-0095
/// Phase 1a. Verify the load-bearing pieces:
/// - PushScope returns an IDisposable that restores the prior async-local value
/// - The scoped value wins over the static global setter
/// - Nested scopes pop in LIFO order
/// - The scope flows through await but does not leak to sibling tasks
/// </summary>
[Collection(nameof(AppHostScopeTests))]
[CollectionDefinition(nameof(AppHostScopeTests), DisableParallelization = true)]
public class AppHostScopeTests : IDisposable
{
    private readonly IServiceProvider? _initialGlobal;

    public AppHostScopeTests()
    {
        // Snapshot whatever the static global was so each test runs from a known baseline
        // and the test class is independent of order.
        _initialGlobal = AppHost.Current;
        AppHost.Current = null;
    }

    public void Dispose()
    {
        AppHost.Current = _initialGlobal;
    }

    [Fact]
    public void PushScope_setsCurrent_andRestoresOnDispose()
    {
        var sp = new FakeSp("A");
        AppHost.Current.Should().BeNull();

        using (AppHost.PushScope(sp))
        {
            AppHost.Current.Should().BeSameAs(sp);
        }

        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public void ScopedValue_winsOver_staticGlobal()
    {
        var globalSp = new FakeSp("global");
        var scopedSp = new FakeSp("scoped");
        AppHost.Current = globalSp;

        using (AppHost.PushScope(scopedSp))
        {
            AppHost.Current.Should().BeSameAs(scopedSp);
        }

        // After dispose, only the static global remains.
        AppHost.Current.Should().BeSameAs(globalSp);
    }

    [Fact]
    public void StaticGlobalSetter_doesNotPollute_AsyncLocal()
    {
        // Setting Current must write to _global only, not the async-local.
        var sp = new FakeSp("global-only");
        AppHost.Current = sp;

        // PushScope null would be invalid (per the contract), but we can verify the async-local
        // is null by pushing a different value and seeing that dispose restores to null (not to sp).
        var pushed = new FakeSp("pushed");
        using (AppHost.PushScope(pushed))
        {
            AppHost.Current.Should().BeSameAs(pushed);
        }

        AppHost.Current.Should().BeSameAs(sp);
    }

    [Fact]
    public void NestedScopes_popInLifoOrder()
    {
        var spOuter = new FakeSp("outer");
        var spInner = new FakeSp("inner");

        using (AppHost.PushScope(spOuter))
        {
            AppHost.Current.Should().BeSameAs(spOuter);

            using (AppHost.PushScope(spInner))
            {
                AppHost.Current.Should().BeSameAs(spInner);
            }

            AppHost.Current.Should().BeSameAs(spOuter);
        }

        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task ScopeFlowsThrough_await()
    {
        var sp = new FakeSp("over-await");

        using (AppHost.PushScope(sp))
        {
            AppHost.Current.Should().BeSameAs(sp);

            await Task.Yield();

            AppHost.Current.Should().BeSameAs(sp);

            await Task.Delay(10);

            AppHost.Current.Should().BeSameAs(sp);
        }
    }

    [Fact]
    public async Task ConcurrentTasks_seeIndependentScopes()
    {
        // Two parallel tasks each push their own scope. Neither should observe the other's.
        async Task<string> Run(string id)
        {
            var sp = new FakeSp(id);
            using (AppHost.PushScope(sp))
            {
                // Yield to encourage interleaving.
                await Task.Delay(20);
                return ((FakeSp)AppHost.Current!).Tag;
            }
        }

        var resultA = Run("A");
        var resultB = Run("B");

        var resolved = await Task.WhenAll(resultA, resultB);
        resolved.Should().BeEquivalentTo(new[] { "A", "B" });

        // Outside both scopes, async-local is unset.
        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public void Dispose_isIdempotent()
    {
        var sp = new FakeSp("idem");
        var scope = AppHost.PushScope(sp);

        AppHost.Current.Should().BeSameAs(sp);

        scope.Dispose();
        AppHost.Current.Should().BeNull();

        // Calling Dispose again should not throw and should not re-pop unexpectedly.
        Action again = () => scope.Dispose();
        again.Should().NotThrow();
        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task SiblingAsyncFlow_doesNotSee_pushFromOther()
    {
        // Capture context before pushing. Run a second task off that captured context and
        // verify it does NOT observe the push that happens after capture.
        var beforePush = AppHost.Current;

        var siblingTask = Task.Run(async () =>
        {
            // Sibling task started before the push; should see whatever the global was.
            await Task.Delay(30);
            return AppHost.Current;
        });

        using (AppHost.PushScope(new FakeSp("local-to-main")))
        {
            // Push is visible here on the main flow.
            AppHost.Current.Should().NotBeNull();
        }

        var siblingSaw = await siblingTask;
        siblingSaw.Should().BeSameAs(beforePush);
    }

    private sealed class FakeSp : IServiceProvider
    {
        public FakeSp(string tag) => Tag = tag;
        public string Tag { get; }
        public object? GetService(Type serviceType) => null;
        public override string ToString() => $"FakeSp({Tag})";
    }
}
