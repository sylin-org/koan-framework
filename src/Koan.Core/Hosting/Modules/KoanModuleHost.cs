using Microsoft.Extensions.Hosting;
using Koan.Core.Semantics;

namespace Koan.Core.Hosting.Modules;

/// <summary>
/// Runs every active <see cref="KoanModule"/>'s <see cref="KoanModule.Start"/> once at host startup, in the same
/// <c>[Before]</c>/<c>[After]</c> topological order used for registration. The modules start in
/// <see cref="StartingAsync"/>, which the host runs before any hosted service's <c>StartAsync</c>, so state a module
/// establishes (Data's hydrated routes) is in place for every hosted workload, whatever order the application registered
/// its hosted services in (ARCH-0140).
/// </summary>
internal sealed class KoanModuleHost : IHostedLifecycleService
{
    private readonly IServiceProvider _services;
    private readonly SemanticModuleRuntime _modules;

    public KoanModuleHost(
        IServiceProvider services,
        SemanticModuleRuntime modules)
    {
        _services = services;
        _modules = modules;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        foreach (var type in _modules.OrderLifecycleTypes())
        {
            if (_modules.TryGetModule(type, out var module))
                await module.Start(_services, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
