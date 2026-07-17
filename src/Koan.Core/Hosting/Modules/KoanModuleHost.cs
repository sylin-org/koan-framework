using Microsoft.Extensions.Hosting;
using Koan.Core.Semantics;

namespace Koan.Core.Hosting.Modules;

/// <summary>
/// Runs every active <see cref="KoanModule"/>'s <see cref="KoanModule.Start"/> once at host startup, in the same
/// <c>[Before]</c>/<c>[After]</c> topological order used for registration.
/// </summary>
internal sealed class KoanModuleHost : IHostedService
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var type in _modules.OrderLifecycleTypes())
        {
            if (_modules.TryGetModule(type, out var module))
                await module.Start(_services, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
