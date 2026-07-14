using System.Collections.Concurrent;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Identity.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Identity.Tests;

[Collection("identity")]
public sealed class IdentityModuleHostOwnershipSpec(IdentityHostFixture fixture)
{
    [Fact]
    public async Task Parallel_module_starts_use_their_supplied_provider_and_restore_the_attached_host()
    {
        var firstReconciler = new CapturingReconciler();
        var secondReconciler = new CapturingReconciler();
        using var firstHost = BuildStartupHost(firstReconciler);
        using var secondHost = BuildStartupHost(secondReconciler);

        var attachedHost = AppHost.Current;
        attachedHost.Should().NotBeNull();
        attachedHost!.GetService(typeof(IIdentityReconciler)).Should()
            .BeSameAs(fixture.Services.GetRequiredService<IIdentityReconciler>());

        await Task.WhenAll(
            new SecIdentityModule().Start(firstHost.Services, TestContext.Current.CancellationToken),
            new SecIdentityModule().Start(secondHost.Services, TestContext.Current.CancellationToken));

        firstReconciler.ObservedProviders.Should().HaveCount(3)
            .And.OnlyContain(provider => ReferenceEquals(provider, firstHost.Services));
        secondReconciler.ObservedProviders.Should().HaveCount(3)
            .And.OnlyContain(provider => ReferenceEquals(provider, secondHost.Services));
        AppHost.Current.Should().BeSameAs(attachedHost);
    }

    private static IHost BuildStartupHost(CapturingReconciler reconciler)
        => new HostBuilder()
            .UseEnvironment(Environments.Development)
            .ConfigureServices(services => services.AddSingleton<IIdentityReconciler>(reconciler))
            .Build();

    private sealed class CapturingReconciler : IIdentityReconciler
    {
        private readonly ConcurrentBag<IServiceProvider?> _observedProviders = [];

        public IReadOnlyCollection<IServiceProvider?> ObservedProviders => _observedProviders;

        public async Task<Identity> ReconcileAsync(IdentityClaims claims, CancellationToken ct = default)
        {
            await Task.Yield();
            _observedProviders.Add(AppHost.Current);
            return new Identity { Id = claims.Subject };
        }
    }
}
