using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Connector.InMemory.Tests.Specs.Hosting;

public sealed class KoanDataSpecHostOwnershipSpec(InMemoryFixture fixture, ITestOutputHelper output)
    : KoanDataSpec<InMemoryFixture>(fixture, output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Boot_preserves_a_newer_owner_attached_during_host_start(bool useExtraSettingsOverload)
    {
        AppHost.Current.Should().BeNull();

        using var markerServices = new ServiceCollection().BuildServiceProvider();
        using var marker = new NewerOwnerHostedService(markerServices);
        Action<IServiceCollection> configure = services => services.AddSingleton<IHostedService>(marker);

        await using (var host = useExtraSettingsOverload
                         ? await BootAsync(Array.Empty<KeyValuePair<string, string?>>(), configure)
                         : await BootAsync(configure))
        {
            AppHost.Current.Should().BeSameAs(markerServices);
        }

        AppHost.Current.Should().BeNull();
    }

    [Fact]
    public async Task Disposing_an_older_bound_host_does_not_clear_the_newer_host()
    {
        AppHost.Current.Should().BeNull();

        var olderMarker = new HostMarker("older");
        var newerMarker = new HostMarker("newer");
        var older = await BootAsync(services => services.AddSingleton(olderMarker));
        var newer = await BootAsync(services => services.AddSingleton(newerMarker));

        try
        {
            AppHost.Current!.GetRequiredService<HostMarker>().Should().BeSameAs(newerMarker);

            await older.DisposeAsync();

            AppHost.Current!.GetRequiredService<HostMarker>().Should().BeSameAs(newerMarker);
        }
        finally
        {
            await newer.DisposeAsync();
            await older.DisposeAsync();
        }

        AppHost.Current.Should().BeNull();
    }

    private sealed record HostMarker(string Name);

    private sealed class NewerOwnerHostedService(IServiceProvider services) : IHostedService, IDisposable
    {
        private IDisposable? _lease;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
            _lease = AppHost.Attach(services);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}
