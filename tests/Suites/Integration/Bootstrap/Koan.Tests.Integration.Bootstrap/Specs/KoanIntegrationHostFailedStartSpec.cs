using System.Threading;
using AwesomeAssertions;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

public sealed class KoanIntegrationHostFailedStartSpec
{
    [Fact]
    public async Task Failed_start_rethrows_original_error_after_disposing_async_owned_services()
    {
        var owned = new AsyncOwnedService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            KoanIntegrationHost.Configure()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(_ => owned);
                    services.AddHostedService<StartupFailureHostedService>();
                })
                .StartAsync(TestContext.Current.CancellationToken));

        exception.Message.Should().Be(StartupFailureHostedService.FailureMessage);
        owned.IsDisposed.Should().BeTrue(
            "the builder owns and must dispose a host that never starts successfully");
    }

    [Fact]
    public async Task Failed_stop_propagates_after_disposing_async_owned_services()
    {
        var owned = new AsyncOwnedService();
        var host = await KoanIntegrationHost.Configure()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_ => owned);
                services.AddHostedService<StopFailureHostedService>();
            })
            .StartAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => host.DisposeAsync().AsTask());

        exception.Message.Should().Be(StopFailureHostedService.FailureMessage);
        owned.IsDisposed.Should().BeTrue(
            "a teardown failure must remain visible without leaking resources owned by the host");
    }

    private sealed class AsyncOwnedService : IAsyncDisposable
    {
        private int _disposed;

        public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposed, 1);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StartupFailureHostedService(AsyncOwnedService owned) : IHostedService
    {
        public const string FailureMessage = "integration-host-startup-probe";

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = owned;
            throw new InvalidOperationException(FailureMessage);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StopFailureHostedService(AsyncOwnedService owned) : IHostedService
    {
        public const string FailureMessage = "integration-host-stop-probe";

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = owned;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(FailureMessage));
    }
}
