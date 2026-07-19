using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Extensions.GenericControllers;

/// <summary>
/// Registry and helpers to expose generic capability controllers without bespoke per-entity classes.
/// </summary>
public static class GenericControllers
{
    public static IServiceCollection AddEntityAuditController<TEntity>(this IServiceCollection services, string routePrefix)
        where TEntity : class
    {
        var genericDef = typeof(Controllers.EntityAuditController<>);
        GenericControllerRegistry.GetOrAdd(services).Register(genericDef, typeof(TEntity), null, routePrefix);
        return services;
    }

    public static IServiceCollection AddEntityModerationController<TEntity, TKey>(this IServiceCollection services, string routePrefix)
        where TEntity : class
        where TKey : notnull
    {
        var genericDef = typeof(Controllers.EntityModerationController<,>);
        GenericControllerRegistry.GetOrAdd(services).Register(genericDef, typeof(TEntity), typeof(TKey), routePrefix);
        return services;
    }

    /// <summary>
    /// Register any generic controller definition with a single entity type argument and a route prefix.
    /// </summary>
    public static IServiceCollection AddGenericController<TEntity>(this IServiceCollection services, Type genericControllerDefinition, string routePrefix)
        where TEntity : class
    {
        if (genericControllerDefinition is null) throw new ArgumentNullException(nameof(genericControllerDefinition));
        if (!genericControllerDefinition.IsGenericTypeDefinition)
            throw new ArgumentException("Must be a generic type definition", nameof(genericControllerDefinition));
        GenericControllerRegistry.GetOrAdd(services).Register(genericControllerDefinition, typeof(TEntity), null, routePrefix);
        return services;
    }

    /// <summary>
    /// ARCH-0092 (§B): register the terse <see cref="RestEntityController{TEntity,TKey}"/> closure for an
    /// entity discovered via <c>[RestEntity]</c>. Non-generic because the entity/key are runtime types
    /// resolved by reflection during discovery.
    /// </summary>
    internal static IServiceCollection AddRestEntityController(this IServiceCollection services, Type entityType, Type keyType, string routePrefix)
    {
        if (entityType is null) throw new ArgumentNullException(nameof(entityType));
        if (keyType is null) throw new ArgumentNullException(nameof(keyType));
        var genericDef = typeof(RestEntityController<,>);
        GenericControllerRegistry.GetOrAdd(services).Register(genericDef, entityType, keyType, routePrefix);
        return services;
    }

    /// <summary>
    /// Adds closed generic controller types for all registered entries.
    /// </summary>
    internal sealed class FeatureProvider(GenericControllerRegistry registry) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var r in registry.Registrations)
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
    internal sealed class RouteConvention(GenericControllerRegistry registry) : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            var type = controller.ControllerType;
            if (!type.IsConstructedGenericType) return;
            var genDef = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            var entity = args[0];
            var key = args.Length > 1 ? args[1] : null;
            var match = registry.Registrations.FirstOrDefault(r => r.GenericDefinition == genDef && r.EntityType == entity && r.KeyType == key);
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
