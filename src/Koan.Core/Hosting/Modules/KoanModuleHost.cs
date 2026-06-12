using Microsoft.Extensions.Hosting;
using Koan.Core.Ordering;

namespace Koan.Core.Hosting.Modules;

/// <summary>
/// Runs every <see cref="KoanModule"/>'s <see cref="KoanModule.Start"/> once at host startup, in the same
/// <c>[Before]</c>/<c>[After]</c> topological order used for registration (ARCH-0086). Registered once
/// (idempotently) by the <see cref="KoanModule"/> bridge; absent when no module extends <see cref="KoanModule"/>.
/// </summary>
internal sealed class KoanModuleHost : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<KoanModule> _modules;

    public KoanModuleHost(IServiceProvider services, IEnumerable<KoanModule> modules)
    {
        _services = services;
        _modules = modules as IReadOnlyList<KoanModule> ?? modules.ToList();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_modules.Count == 0) return;

        // Order by the same topology as registration; RegistrarOrdering operates on types, so map back to instances.
        var byType = _modules
            .GroupBy(m => m.GetType())
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var type in RegistrarOrdering.Sort(byType.Keys))
        {
            if (byType.TryGetValue(type, out var module))
                await module.Start(_services, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
