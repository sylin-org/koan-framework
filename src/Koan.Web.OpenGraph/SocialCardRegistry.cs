using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.OpenGraph;

/// <summary>
/// Host-owned, type-keyed card registry. Application declarations are written during composition and
/// the same host reads the immutable registration order at request time.
/// </summary>
internal sealed class SocialCardRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, CardRegistration> _byType = new();
    private List<CardRegistration> _ordered = new();

    public static SocialCardRegistry GetOrCreate(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services
            .Where(static descriptor => descriptor.ServiceType == typeof(SocialCardRegistry))
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<SocialCardRegistry>()
            .LastOrDefault();
        if (existing is not null) return existing;

        var registry = new SocialCardRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>Registrations in registration order; first match wins at request time.</summary>
    public IReadOnlyList<CardRegistration> Registrations
    {
        get { lock (_gate) { return _ordered; } }
    }

    public bool Has(Type type)
    {
        lock (_gate) { return _byType.ContainsKey(type); }
    }

    public bool TryGet(Type type, out CardRegistration registration)
    {
        lock (_gate) { return _byType.TryGetValue(type, out registration!); }
    }

    public void Register(Type type, CardRegistration registration)
    {
        lock (_gate)
        {
            if (_byType.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"A social card is already registered for '{type.Name}' in this application. Register one card per type.");
            }

            _byType[type] = registration;
            // Copy-on-write so a concurrent reader iterating Registrations is never disturbed.
            _ordered = new List<CardRegistration>(_ordered) { registration };
        }
    }
}
