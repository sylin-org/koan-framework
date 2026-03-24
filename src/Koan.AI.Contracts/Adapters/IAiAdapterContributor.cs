using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.AI.Contracts.Adapters;

/// <summary>
/// Discovers and registers AI adapters during application startup.
/// Implementations run inside the Koan.AI bootstrapper to populate the adapter registry.
/// </summary>
public interface IAiAdapterContributor
{
    /// <summary>
    /// Executes the contributor logic using the provided service scope.
    /// Implementations may resolve required services from the scope and update registries.
    /// </summary>
    /// <param name="services">Scoped service provider for resolving dependencies.</param>
    /// <param name="cancellationToken">Signals when startup is shutting down.</param>
    ValueTask Contribute(IServiceProvider services, CancellationToken cancellationToken);
}
