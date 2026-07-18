using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Extensions.GenericControllers;

/// <summary>
/// Owns the generic controller declarations for one application service collection.
/// </summary>
internal sealed class GenericControllerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<ControllerKey, Registration> _registrations = new();

    public IReadOnlyCollection<Registration> Registrations
    {
        get
        {
            lock (_gate)
            {
                return _registrations.Values.ToArray();
            }
        }
    }

    public void Register(Type genericDefinition, Type entityType, Type? keyType, string routePrefix)
    {
        ArgumentNullException.ThrowIfNull(genericDefinition);
        ArgumentNullException.ThrowIfNull(entityType);

        if (string.IsNullOrWhiteSpace(routePrefix))
        {
            throw new ArgumentException("A non-empty route prefix is required.", nameof(routePrefix));
        }

        var route = routePrefix.Trim();
        var key = new ControllerKey(genericDefinition, entityType, keyType);

        lock (_gate)
        {
            if (_registrations.TryGetValue(key, out var existing))
            {
                if (string.Equals(existing.RoutePrefix, route, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Generic controller '{genericDefinition.Name}' for entity '{entityType.FullName}' is already " +
                    $"registered at '{existing.RoutePrefix}'. Keep one route or declare an explicit controller for " +
                    "multiple route projections.");
            }

            _registrations.Add(key, new Registration(genericDefinition, entityType, keyType, route));
        }
    }

    public static GenericControllerRegistry GetOrAdd(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(GenericControllerRegistry))?
            .ImplementationInstance as GenericControllerRegistry;

        if (existing is not null)
        {
            return existing;
        }

        var registry = new GenericControllerRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    internal sealed record Registration(
        Type GenericDefinition,
        Type EntityType,
        Type? KeyType,
        string RoutePrefix);

    private readonly record struct ControllerKey(
        Type GenericDefinition,
        Type EntityType,
        Type? KeyType);
}
