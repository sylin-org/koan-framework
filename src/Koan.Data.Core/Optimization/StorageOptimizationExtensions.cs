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
        // DEBUG: MediaFormat specific logging
        if (typeof(TEntity).Name == "MediaFormat")
        {
            Console.WriteLine($"[OPTIMIZATION-DEBUG] GetStorageOptimization called for MediaFormat<{typeof(TKey).Name}>");
        }

        var result = AggregateBags.GetOrAdd<TEntity, TKey, StorageOptimizationInfo>(
            serviceProvider,
            OptimizationBagKey,
            () => AnalyzeEntityOptimization<TEntity, TKey>());

        if (typeof(TEntity).Name == "MediaFormat")
        {
            Console.WriteLine($"[OPTIMIZATION-DEBUG] GetStorageOptimization returning for MediaFormat: {result.OptimizationType}");
        }

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

        // DEBUG: MediaFormat specific logging
        if (entityType.Name == "MediaFormat")
        {
            Console.WriteLine($"[OPTIMIZATION-DEBUG] Analyzing MediaFormat - EntityType: {entityType.FullName}, KeyType: {keyType.Name}");
        }

        // Only optimize string-keyed entities
        if (keyType != typeof(string))
        {
            if (entityType.Name == "MediaFormat")
                Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - KeyType {keyType.Name} is not string, returning None");
            return StorageOptimizationInfo.None;
        }

        // Get ID property name from framework metadata
        var idSpec = AggregateMetadata.GetIdSpec(entityType);
        if (idSpec == null)
        {
            if (entityType.Name == "MediaFormat")
                Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - idSpec is null, returning None");
            return StorageOptimizationInfo.None;
        }

        if (entityType.Name == "MediaFormat")
        {
            Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - idSpec found: {idSpec.Prop.Name}");
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
            if (entityType.Name == "MediaFormat")
                Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Calling AnalyzeStringKeyedEntity for idProperty: {idSpec.Prop.Name}");

            var result = AnalyzeStringKeyedEntity<TEntity>(idSpec.Prop.Name);

            if (entityType.Name == "MediaFormat")
                Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - AnalyzeStringKeyedEntity returned: {result.OptimizationType}, Reason: {result.Reason}");

            return result;
        }

        if (entityType.Name == "MediaFormat")
            Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Key type not string, returning None");

        return StorageOptimizationInfo.None;
    }

    /// <summary>
    /// Analyzes string-keyed entities to determine if they should be optimized.
    /// Only Entity&lt;Model&gt; (implicit string) gets optimization, not Entity&lt;Model, string&gt; (explicit string).
    /// </summary>
    private static StorageOptimizationInfo AnalyzeStringKeyedEntity<TEntity>(string idPropertyName)
    {
        var entityType = typeof(TEntity);

        // DEBUG: MediaFormat specific logging
        if (entityType.Name == "MediaFormat")
        {
            Console.WriteLine($"[OPTIMIZATION-DEBUG] AnalyzeStringKeyedEntity - Starting analysis for MediaFormat");
            Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - EntityType: {entityType.FullName}, IdProperty: {idPropertyName}");
        }

        // Walk up the inheritance chain to find Entity base classes
        var baseType = entityType.BaseType;
        int level = 0;
        while (baseType != null)
        {
            if (entityType.Name == "MediaFormat")
            {
                Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Level {level}: Checking baseType: {baseType.FullName}, IsGenericType: {baseType.IsGenericType}");
            }

            if (baseType.IsGenericType)
            {
                var genericTypeDef = baseType.GetGenericTypeDefinition();
                var genericArgs = baseType.GetGenericArguments();

                if (entityType.Name == "MediaFormat")
                {
                    Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Level {level}: GenericTypeDef: {genericTypeDef.Name}, GenericArgs.Length: {genericArgs.Length}");
                    for (int i = 0; i < genericArgs.Length; i++)
                    {
                        Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Level {level}: GenericArgs[{i}]: {genericArgs[i].Name}");
                    }
                }

                // Look for Entity base classes
                if (genericTypeDef.Name.StartsWith("Entity", StringComparison.Ordinal))
                {
                    if (entityType.Name == "MediaFormat")
                    {
                        Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Level {level}: Found Entity base class: {genericTypeDef.Name}");
                    }

                    // Check for Entity<TEntity> pattern (single generic - implicit string)
                    if (genericArgs.Length == 1 && genericArgs[0] == entityType)
                    {
                        if (entityType.Name == "MediaFormat")
                        {
                            Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Level {level}: MATCH! Entity<T> pattern detected - returning GUID optimization");
                        }

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
                        if (entityType.Name == "MediaFormat")
                        {
                            Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Level {level}: MATCH! Entity<T, string> pattern detected - returning NO optimization");
                        }

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

            if (entityType.Name == "MediaFormat")
            {
                Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Moving to next level {level}, baseType: {baseType?.FullName ?? "null"}");
            }
        }

        if (entityType.Name == "MediaFormat")
        {
            Console.WriteLine($"[OPTIMIZATION-DEBUG] MediaFormat - Reached end of inheritance chain - returning NO optimization for IEntity<string>");
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