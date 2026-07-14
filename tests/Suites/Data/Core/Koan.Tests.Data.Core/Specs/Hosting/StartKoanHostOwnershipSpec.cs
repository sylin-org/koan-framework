using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Tests.Data.Core.Specs.Hosting;

public sealed class StartKoanHostOwnershipSpec : IDisposable
{
    private readonly IServiceProvider? _initialHost = AppHost.Current;

    public StartKoanHostOwnershipSpec()
    {
        AppHost.Current = null;
    }

    public void Dispose()
    {
        AppHost.Current = _initialHost;
    }

    [Fact]
    public void Disposing_started_provider_releases_ambient_owner()
    {
        var services = new ServiceCollection();
        services.AddSingleton<OwnedDisposable>();
        var provider = services.StartKoan();
        var owned = provider.GetRequiredService<OwnedDisposable>();

        try
        {
            AppHost.Current.Should().BeSameAs(provider);

            Dispose(provider);

            AppHost.Current.Should().BeNull();
            owned.IsDisposed.Should().BeTrue();
            owned.SawReleasedHost.Should().BeTrue();
        }
        finally
        {
            Dispose(provider);
            if (ReferenceEquals(AppHost.Current, provider))
            {
                AppHost.Current = null;
            }
        }
    }

    [Fact]
    public void Disposing_older_provider_cannot_clear_newer_owner()
    {
        var older = Start("older");
        var newer = Start("newer");

        try
        {
            AppHost.Current.Should().BeSameAs(newer);

            Dispose(older);

            AppHost.Current.Should().BeSameAs(newer);

            Dispose(newer);

            AppHost.Current.Should().BeNull();
        }
        finally
        {
            Dispose(newer);
            Dispose(older);
            AppHost.Current = null;
        }
    }

    [Fact]
    public async Task Parallel_flow_scopes_use_their_provider_and_concurrent_disposal_releases_global_owner()
    {
        var first = Start("first");
        var second = Start("second");

        try
        {
            static async Task<string> ResolveOwner(IServiceProvider provider)
            {
                using var scope = AppHost.PushScope(provider);
                await Task.Yield();
                return AppHost.Current!.GetRequiredService<HostMarker>().Owner;
            }

            var owners = await Task.WhenAll(ResolveOwner(first), ResolveOwner(second));
            owners.Should().BeEquivalentTo(["first", "second"]);

            await Task.WhenAll(
                Task.Run(() => Dispose(first)),
                Task.Run(() => Dispose(second)));

            AppHost.Current.Should().BeNull();
        }
        finally
        {
            Dispose(second);
            Dispose(first);
            AppHost.Current = null;
        }
    }

    [Fact]
    public void Failed_runtime_start_releases_provider_and_owned_services()
    {
        IServiceProvider? capturedProvider = null;
        OwnedDisposable? owned = null;
        var services = new ServiceCollection();
        services.AddSingleton<OwnedDisposable>();
        services.AddSingleton<IAppRuntime>(provider =>
        {
            capturedProvider = provider;
            owned = provider.GetRequiredService<OwnedDisposable>();
            return new ThrowingRuntime();
        });

        try
        {
            Action start = () => services.StartKoan();

            start.Should().Throw<InvalidOperationException>().WithMessage("start-koan-probe");
            AppHost.Current.Should().BeNull();
            owned.Should().NotBeNull();
            owned!.IsDisposed.Should().BeTrue();
            owned.SawReleasedHost.Should().BeTrue();
        }
        finally
        {
            if (capturedProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            AppHost.Current = null;
        }
    }

    private static IServiceProvider Start(string owner)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HostMarker(owner));
        return services.StartKoan();
    }

    private static void Dispose(IServiceProvider provider)
    {
        ((IDisposable)provider).Dispose();
    }

    private sealed record HostMarker(string Owner);

    private sealed class OwnedDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public bool SawReleasedHost { get; private set; }

        public void Dispose()
        {
            SawReleasedHost = AppHost.Current is null;
            IsDisposed = true;
        }
    }

    private sealed class ThrowingRuntime : IAppRuntime
    {
        public void Discover()
        {
            throw new InvalidOperationException("start-koan-probe");
        }

        public void Start()
        {
        }
    }
}
