using Koan.Communication;
using Koan.Communication.Infrastructure;
using Koan.Communication.Runtime;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Model;

/// <summary>A terminal-only Entity Events facet; it states typed business occurrences.</summary>
public readonly struct EntityEventsFacet<TEntity>
    where TEntity : class, IEntity
{
    private readonly IAsyncEnumerable<TEntity> _source;

    internal EntityEventsFacet(IAsyncEnumerable<TEntity> source)
        => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Raises a new payloadless occurrence for every Entity accepted from the source.
    /// A details-required event contract is rejected before source enumeration.
    /// </summary>
    /// <param name="ct">Stops source enumeration and publication.</param>
    /// <param name="channel">A startup-declared business channel, or null for the inferred default.</param>
    public Task<EventAcceptance> Raise<TEvent>(CancellationToken ct = default, string? channel = null)
        where TEvent : class
        => AppHost.GetRequiredService<EventCoordinator>(Constants.Operations.Raise)
            .Raise<TEntity, TEvent>(_source, details: null, hasDetails: false, channel, ct);

    /// <summary>Raises a new occurrence with explicit business details for every accepted Entity.</summary>
    /// <param name="details">Business details not already represented by the Entity snapshot.</param>
    /// <param name="ct">Stops source enumeration and publication.</param>
    /// <param name="channel">A startup-declared business channel, or null for the inferred default.</param>
    public Task<EventAcceptance> Raise<TEvent>(TEvent details, CancellationToken ct = default, string? channel = null)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(details);
        return AppHost.GetRequiredService<EventCoordinator>(Constants.Operations.Raise)
            .Raise(_source, details, hasDetails: true, channel, ct);
    }
}
