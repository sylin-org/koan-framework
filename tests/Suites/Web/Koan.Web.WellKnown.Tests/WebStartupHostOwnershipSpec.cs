using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Web.WellKnown.Tests;

public sealed class WebStartupHostOwnershipSpec
{
    [Fact]
    public async Task Pipeline_construction_preserves_a_newer_attached_owner()
    {
        AppHost.Current.Should().BeNull();

        using var markerServices = new ServiceCollection().BuildServiceProvider();
        using var filter = new NewerOwnerStartupFilter(markerServices);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment("Test");
                web.ConfigureServices(services =>
                {
                    // Startup filters wrap in registration order. Register this first so it attaches
                    // a newer owner immediately before Koan's filter constructs the inner pipeline.
                    services.AddSingleton<IStartupFilter>(filter);
                    services.AddKoan();
                });
                web.Configure(_ => { });
            })
            .Build();

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);

            filter.ObservedAfterNext.Should().BeSameAs(markerServices);
            AppHost.Current.Should().BeSameAs(markerServices);
        }
        finally
        {
            try
            {
                await host.StopAsync(CancellationToken.None);
            }
            finally
            {
                filter.Dispose();
            }
        }

        AppHost.Current.Should().BeNull();
    }

    private sealed class NewerOwnerStartupFilter(IServiceProvider services) : IStartupFilter, IDisposable
    {
        private IDisposable? _lease;

        public IServiceProvider? ObservedAfterNext { get; private set; }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                Interlocked.Exchange(ref _lease, null)?.Dispose();
                _lease = AppHost.Attach(services);

                next(app);

                ObservedAfterNext = AppHost.Current;
            };
        }

        public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
    }
}
