using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Xunit;

namespace Koan.Core.Tests.Hosting;

/// <summary>
/// Verifies that framework-managed host binding has an explicit owner and cannot retain or clear
/// another host's provider.
/// </summary>
[Collection(nameof(AppHostScopeTests))]
public sealed class AppHostBinderHostedServiceTests : IDisposable
{
    private readonly IServiceProvider? _initialGlobal = AppHost.Current;

    public AppHostBinderHostedServiceTests()
    {
        AppHost.Current = null;
    }

    public void Dispose()
    {
        AppHost.Current = _initialGlobal;
    }

    [Fact]
    public async Task Start_replaces_stale_provider_andStop_releases_its_lease()
    {
        var stale = new FakeServiceProvider("stale");
        var current = new FakeServiceProvider("current");
        AppHost.Current = stale;
        var binder = new AppHostBinderHostedService(current);

        await binder.StartAsync(TestContext.Current.CancellationToken);
        AppHost.Current.Should().BeSameAs(current);

        await binder.StopAsync(TestContext.Current.CancellationToken);
        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task Older_host_cannot_clear_the_newer_hosts_provider()
    {
        var providerA = new FakeServiceProvider("A");
        var providerB = new FakeServiceProvider("B");
        var binderA = new AppHostBinderHostedService(providerA);
        var binderB = new AppHostBinderHostedService(providerB);

        await binderA.StartAsync(TestContext.Current.CancellationToken);
        await binderB.StartAsync(TestContext.Current.CancellationToken);
        AppHost.Current.Should().BeSameAs(providerB);

        await binderA.StopAsync(TestContext.Current.CancellationToken);
        AppHost.Current.Should().BeSameAs(providerB);

        await binderB.StopAsync(TestContext.Current.CancellationToken);
        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task Flow_scope_wins_over_attached_host_without_changing_lease_ownership()
    {
        var attached = new FakeServiceProvider("attached");
        var scoped = new FakeServiceProvider("scoped");
        var binder = new AppHostBinderHostedService(attached);
        await binder.StartAsync(TestContext.Current.CancellationToken);

        using (AppHost.PushScope(scoped))
        {
            AppHost.Current.Should().BeSameAs(scoped);
        }

        AppHost.Current.Should().BeSameAs(attached);
        await binder.StopAsync(TestContext.Current.CancellationToken);
        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task Identity_follows_sequential_host_ownership()
    {
        var providerA = new FakeServiceProvider("A", Identity("app-a"));
        var providerB = new FakeServiceProvider("B", Identity("app-b"));
        var binderA = new AppHostBinderHostedService(providerA);
        var binderB = new AppHostBinderHostedService(providerB);

        await binderA.StartAsync(TestContext.Current.CancellationToken);
        AppHost.Identity.Code.Should().Be("app-a");
        await binderA.StopAsync(TestContext.Current.CancellationToken);

        await binderB.StartAsync(TestContext.Current.CancellationToken);
        AppHost.Identity.Code.Should().Be("app-b");
        await binderB.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Parallel_flow_scopes_see_their_own_host_identity()
    {
        var attached = new FakeServiceProvider("attached", Identity("attached"));
        var providerA = new FakeServiceProvider("A", Identity("app-a"));
        var providerB = new FakeServiceProvider("B", Identity("app-b"));
        var binder = new AppHostBinderHostedService(attached);
        await binder.StartAsync(TestContext.Current.CancellationToken);

        async Task<string> ResolveInScope(FakeServiceProvider provider)
        {
            using (AppHost.PushScope(provider))
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
                return AppHost.Identity.Code;
            }
        }

        var resolved = await Task.WhenAll(ResolveInScope(providerA), ResolveInScope(providerB));
        resolved.Should().Equal("app-a", "app-b");

        AppHost.Identity.Code.Should().Be("attached");
        await binder.StopAsync(TestContext.Current.CancellationToken);
    }

    private static ApplicationIdentitySnapshot Identity(string code) => new(
        $"Application {code}",
        code,
        "",
        null,
        null,
        []);

    private sealed class FakeServiceProvider(
        string name,
        ApplicationIdentitySnapshot? identity = null) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ApplicationIdentitySnapshot) ? identity : null;

        public override string ToString() => name;
    }
}
