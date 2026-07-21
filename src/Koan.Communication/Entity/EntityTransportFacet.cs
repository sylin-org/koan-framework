using Koan.Communication;
using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Model;

/// <summary>A terminal-only Entity Transport facet; it does not expose a general flow container.</summary>
public readonly struct EntityTransportFacet<TEntity>
    where TEntity : class, IEntity
{
    private readonly IAsyncEnumerable<TEntity> _source;

    internal EntityTransportFacet(IAsyncEnumerable<TEntity> source)
        => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Publishes each Entity as a new immutable logical snapshot and returns after bounded channel acceptance.
    /// Handler completion is observed separately through the returned acceptance.
    /// </summary>
    /// <param name="ct">Stops source enumeration and publication.</param>
    /// <param name="channel">A startup-declared business channel, or null for the inferred default.</param>
    public Task<TransportAcceptance> Send(CancellationToken ct = default, string? channel = null)
        => AppHost.GetRequiredService<TransportCoordinator>(Constants.Operations.Send)
            .Send(_source, channel, ct);
}
