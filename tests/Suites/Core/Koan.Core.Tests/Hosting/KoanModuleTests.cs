using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Ordering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Core.Tests.Hosting;

/// <summary>Conformance specs for the boot-time module primitive (ARCH-0086).</summary>
public class KoanModuleTests
{
    // Shared recorder — modules are re-instantiated by DI for Start(), so they record to a static.
    private static readonly List<string> StartOrder = new();

    private sealed class ModuleA : KoanModule
    {
        public override string Id => "test.a";
        public override void Register(IServiceCollection services) => services.AddSingleton(new Marker("a"));
        public override Task Start(IServiceProvider services, CancellationToken ct) { StartOrder.Add(Id); return Task.CompletedTask; }
    }

    [After(typeof(ModuleA))]
    private sealed class ModuleB : KoanModule
    {
        public override string Id => "test.b";
        public override Task Start(IServiceProvider services, CancellationToken ct) { StartOrder.Add(Id); return Task.CompletedTask; }
    }

    private sealed record Marker(string Name);

    [Fact]
    public void Bridge_maps_Id_and_Version_onto_IKoanAutoRegistrar()
    {
        var module = new ModuleA();
        var registrar = (IKoanAutoRegistrar)module;
        registrar.ModuleName.Should().Be("test.a");
        registrar.ModuleVersion.Should().Be(module.Version);
    }

    [Fact]
    public void Initialize_calls_Register_and_registers_the_module_plus_host()
    {
        var services = new ServiceCollection();
        ((IKoanInitializer)new ModuleA()).Initialize(services);

        // Register() ran:
        services.Should().Contain(d => d.ServiceType == typeof(Marker));
        // The module instance is resolvable for the host:
        services.Should().Contain(d => d.ServiceType == typeof(KoanModule));
        // The host that runs Start() is registered (as IHostedService):
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task Host_runs_Start_on_every_module_in_Before_After_order()
    {
        StartOrder.Clear();
        var services = new ServiceCollection();
        // Register B before A to prove ordering is by [After], not registration order.
        ((IKoanInitializer)new ModuleB()).Initialize(services);
        ((IKoanInitializer)new ModuleA()).Initialize(services);

        using var provider = services.BuildServiceProvider();
        var host = provider.GetServices<IHostedService>().Single(h => h.GetType().Name == "KoanModuleHost");

        await host.StartAsync(CancellationToken.None);

        StartOrder.Should().Equal("test.a", "test.b"); // A first: B is [After(ModuleA)]
    }
}
