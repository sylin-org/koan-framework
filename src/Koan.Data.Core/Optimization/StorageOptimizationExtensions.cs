using System;
using System.Reflection;
using Koan.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Optimization;

/// <summary>
/// Extensions for accessing storage optimization metadata via AggregateBag.
/// </summary>
public static class StorageOptimizationExtensions
{
    private const string OptimizationBagKey = "StorageOptimization";

    /// <summary>
    /// Gets storage optimization info for an entity type.
    /// Uses AggregateBag for efficient caching - analysis happens once per entity type.
    /// </summary>
    public static StorageOptimizationInfo GetStorageOptimization<TEntity, TKey>(this IServiceProvider serviceProvider)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var result = AggregateBags.GetOrAdd<TEntity, TKey, StorageOptimizationInfo>(
            serviceProvider,
            OptimizationBagKey,
            () => AnalyzeEntityOptimization<TEntity, TKey>());

        return result;
    }

    /// <summary>
    /// Analyzes an entity type for storage optimization at startup.
    /// Only string-keyed entities can be optimized.
    /// </summary>
    private static StorageOptimizationInfo AnalyzeEntityOptimization<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var entityType = typeof(TEntity);
        var keyType = typeof(TKey);

        // Only optimize string-keyed entities
        if (keyType != typeof(string))
        {
            return StorageOptimizationInfo.None;
        }

        // Get ID property name from framework metadata
        var idSpec = AggregateMetadata.GetIdSpec(entityType);
        if (idSpec == null)
        {
            return StorageOptimizationInfo.None;
        }

        // Check for OptimizeStorageAttribute
        var optimizeAttr = entityType.GetCustomAttribute<OptimizeStorageAttribute>(inherit: true);
        if (optimizeAttr != null)
        {
            // Explicit attribute present - respect its settings
            if (optimizeAttr.OptimizationType == StorageOptimizationType.Guid)
            {
                return new StorageOptimizationInfo
                {
                    OptimizationType = StorageOptimizationType.Guid,
                    IdPropertyName = idSpec.Prop.Name,
                    Reason = optimizeAttr.Reason
                };
            }
            else if (optimizeAttr.OptimizationType == StorageOptimizationType.None)
            {
                // Explicitly disabled optimization
                return new StorageOptimizationInfo
                {
                    OptimizationType = StorageOptimizationType.None,
                    IdPropertyName = idSpec.Prop.Name,
                    Reason = optimizeAttr.Reason
                };
            }
        }


        // NEW: Smart detection for Entity<> vs Entity<,string> pattern
        // Only optimize Entity<Model> (implicit string), NOT Entity<Model, string> (explicit string)
        if (typeof(TKey) == typeof(string))
        {
            var result = AnalyzeStringKeyedEntity<TEntity>(idSpec.Prop.Name);
            return result;
        }

        return StorageOptimizationInfo.None;
    }

    /// <summary>
    /// Analyzes string-keyed entities to determine if they should be optimized.
    /// Only Entity&lt;Model&gt; (implicit string) gets optimization, not Entity&lt;Model, string&gt; (explicit string).
    /// </summary>
    private static StorageOptimizationInfo AnalyzeStringKeyedEntity<TEntity>(string idPropertyName)
    {
        var entityType = typeof(TEntity);

        // Walk up the inheritance chain to find Entity base classes
        var baseType = entityType.BaseType;
        int level = 0;
        while (baseType != null)
        {
            if (baseType.IsGenericType)
            {
                var genericTypeDef = baseType.GetGenericTypeDefinition();
                var genericArgs = baseType.GetGenericArguments();

                // Look for Entity base classes
                if (genericTypeDef.Name.StartsWith("Entity", StringComparison.Ordinal))
                {
                    // Check for Entity<TEntity> pattern (single generic - implicit string)
                    if (genericArgs.Length == 1 && genericArgs[0] == entityType)
                    {
                        return new StorageOptimizationInfo
                        {
                            OptimizationType = StorageOptimizationType.Guid,
                            IdPropertyName = idPropertyName,
                            Reason = "Automatic GUID optimization for Entity<T> pattern (implicit string key)"
                        };
                    }

                    // Check for Entity<TEntity, string> pattern (explicit string - don't optimize)
                    if (genericArgs.Length == 2 &&
                        genericArgs[0] == entityType &&
                        genericArgs[1] == typeof(string))
                    {
                        return new StorageOptimizationInfo
                        {
                            OptimizationType = StorageOptimizationType.None,
                            IdPropertyName = idPropertyName,
                            Reason = "No optimization for Entity<T, string> pattern (explicit string key choice)"
                        };
                    }
                }
            }
            level++;
            baseType = baseType.BaseType;
        }

        // For other IEntity<string> implementations, don't optimize (explicit choice)
        return new StorageOptimizationInfo
        {
            OptimizationType = StorageOptimizationType.None,
            IdPropertyName = idPropertyName,
            Reason = "No optimization for direct IEntity<string> implementation (explicit string choice)"
        };
    }
}