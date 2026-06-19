using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Abstractions;
using Koan.Web.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Extensions.GenericControllers;

/// <summary>
/// ARCH-0092 (§B): discovers <c>[RestEntity]</c>-annotated entities and registers a terse full-CRUD
/// controller for each, honoring the precedence rule — an explicit, hand-written
/// <see cref="EntityController{TEntity,TKey}"/> subclass for the same entity wins and the terse
/// registration is skipped.
/// </summary>
/// <remarks>
/// Runs at <c>IKoanAutoRegistrar.Initialize</c> time, which is <i>after</i>
/// <see cref="AssemblyCache"/> is fully populated by the bootstrapper's assembly-closure pass and
/// <i>before</i> MVC materializes the controller feature — so the registration entries are visible to
/// <see cref="GenericControllers.FeatureProvider"/>.
/// </remarks>
internal static class RestEntityRegistration
{
    public static void RegisterDiscovered(IServiceCollection services)
    {
        var types = SafeGetTypes(AssemblyCache.Instance.GetAllAssemblies()).ToArray();
        var explicitlyControlled = DiscoverExplicitControllerEntities(types);

        foreach (var (entityType, attribute) in DiscoverRestEntities(types))
        {
            // Precedence (§B): a hand-written EntityController<T> subclass owns this entity's REST surface.
            if (explicitlyControlled.Contains(entityType)) continue;

            var keyType = ResolveKeyType(entityType);
            if (keyType is null) continue; // not an IEntity<TKey> — nothing to expose

            var route = ResolveRoute(entityType, attribute);
            services.AddRestEntityController(entityType, keyType, route);
        }
    }

    private static IEnumerable<(Type EntityType, RestEntityAttribute Attribute)> DiscoverRestEntities(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition) continue;
            var attr = type.GetCustomAttribute<RestEntityAttribute>(inherit: false);
            if (attr is null) continue;
            yield return (type, attr);
        }
    }

    // Entities already realized by a concrete, hand-written EntityController<T[,K]> subclass.
    private static HashSet<Type> DiscoverExplicitControllerEntities(IEnumerable<Type> types)
    {
        var set = new HashSet<Type>();
        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition) continue;
            var entity = ExtractControllerEntityType(type);
            if (entity is not null) set.Add(entity);
        }
        return set;
    }

    // The entity is the first type arg of the closed EntityController<,> in the base chain. An open generic
    // controller definition (e.g. RestEntityController<,> itself) yields a generic parameter — skip those.
    private static Type? ExtractControllerEntityType(Type controllerType)
    {
        for (var baseType = controllerType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(EntityController<,>))
            {
                var entity = baseType.GetGenericArguments()[0];
                return entity.IsGenericParameter ? null : entity;
            }
        }

        return null;
    }

    private static Type? ResolveKeyType(Type entityType)
        => entityType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>))?
            .GetGenericArguments()
            .FirstOrDefault();

    private static string ResolveRoute(Type entityType, RestEntityAttribute attribute)
        => !string.IsNullOrWhiteSpace(attribute.Route)
            ? attribute.Route!.Trim()
            : $"api/{ToKebabCase(entityType.Name)}";

    private static IEnumerable<Type> SafeGetTypes(Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is not null) yield return type;
            }
        }
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var sb = new StringBuilder(value.Length + 4);
        foreach (var c in value)
        {
            if (char.IsUpper(c) && sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
