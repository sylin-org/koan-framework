using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Sora.Web.Extensions.GenericControllers;

/// <summary>
/// Registry and helpers to expose generic capability controllers without bespoke per-entity classes.
/// </summary>
public static class GenericControllers
{
    private static readonly ConcurrentDictionary<string, Registration> _registrations = new();

    private static string Key(Type genericDef, Type entity, Type? key, string route)
        => string.Join("|", genericDef.AssemblyQualifiedName, entity.AssemblyQualifiedName, key?.AssemblyQualifiedName ?? "", route);

    public static IServiceCollection AddEntityAuditController<TEntity>(this IServiceCollection services, string routePrefix)
        where TEntity : class
    {
        var genericDef = typeof(Controllers.EntityAuditController<>);
        _registrations[Key(genericDef, typeof(TEntity), null, routePrefix)] = new Registration(genericDef, typeof(TEntity), null, routePrefix);
        return services;
    }

    public static IServiceCollection AddEntitySoftDeleteController<TEntity, TKey>(this IServiceCollection services, string routePrefix)
        where TEntity : class
        where TKey : notnull
    {
        var genericDef = typeof(Controllers.EntitySoftDeleteController<,>);
        _registrations[Key(genericDef, typeof(TEntity), typeof(TKey), routePrefix)] = new Registration(genericDef, typeof(TEntity), typeof(TKey), routePrefix);
        return services;
    }

    public static IServiceCollection AddEntityModerationController<TEntity, TKey>(this IServiceCollection services, string routePrefix)
        where TEntity : class
        where TKey : notnull
    {
        var genericDef = typeof(Controllers.EntityModerationController<,>);
        _registrations[Key(genericDef, typeof(TEntity), typeof(TKey), routePrefix)] = new Registration(genericDef, typeof(TEntity), typeof(TKey), routePrefix);
        return services;
    }

    internal static IEnumerable<Registration> Registrations => _registrations.Values;

    internal sealed record Registration(Type GenericDefinition, Type EntityType, Type? KeyType, string RoutePrefix);

    /// <summary>
    /// Adds closed generic controller types for all registered entries.
    /// </summary>
    internal sealed class FeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var r in Registrations)
            {
                Type closed;
                if (r.KeyType is null)
                {
                    closed = r.GenericDefinition.MakeGenericType(r.EntityType);
                }
                else
                {
                    closed = r.GenericDefinition.MakeGenericType(r.EntityType, r.KeyType);
                }
                if (!feature.Controllers.Any(t => t.AsType() == closed))
                {
                    feature.Controllers.Add(closed.GetTypeInfo());
                }
            }
        }
    }

    /// <summary>
    /// Applies route prefix to registered controllers.
    /// </summary>
    internal sealed class RouteConvention : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            var type = controller.ControllerType;
            if (!type.IsConstructedGenericType) return;
            var genDef = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            var entity = args[0];
            var key = args.Length > 1 ? args[1] : null;
            var match = Registrations.FirstOrDefault(r => r.GenericDefinition == genDef && r.EntityType == entity && r.KeyType == key);
            if (match is null) return;

            // Replace or add a selector with the route prefix
            var route = new AttributeRouteModel(new Microsoft.AspNetCore.Mvc.RouteAttribute(match.RoutePrefix));
            if (controller.Selectors.Count == 0)
            {
                controller.Selectors.Add(new SelectorModel { AttributeRouteModel = route });
            }
            else
            {
                foreach (var s in controller.Selectors)
                {
                    s.AttributeRouteModel = route;
                }
            }
        }
    }
}
