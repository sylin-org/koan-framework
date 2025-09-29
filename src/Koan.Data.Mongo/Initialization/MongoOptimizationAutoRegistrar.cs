using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Abstractions;
using Koan.Data.Core.Optimization;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Koan.Data.Mongo.Initialization;

/// <summary>
/// KoanAutoRegistrar that scans all Entity types and applies global MongoDB driver configuration
/// for GUID optimization. Addresses MongoDB .NET driver v3.0+ issues with custom serializers.
/// </summary>
public class MongoOptimizationAutoRegistrar : IKoanInitializer
{
    private static readonly object _lock = new();
    private static bool _globalConfigurationApplied = false;

    public void Initialize(IServiceCollection services)
    {
        lock (_lock)
        {
            if (_globalConfigurationApplied)
            {
                return;
            }

            try
            {
                // Step 1: Apply global MongoDB driver configuration for v3.5.0 compatibility
                ConfigureGlobalMongoDriverSettings();

                // Step 2: Scan all assemblies for Entity types requiring optimization
                // Use direct reflection without requiring a service provider
                var optimizedEntityTypes = ScanForOptimizedEntityTypes();

                // Step 3: Register global serializers for discovered types
                RegisterGlobalSerializers(optimizedEntityTypes);

                _globalConfigurationApplied = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Failed to apply global configuration: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Configures global MongoDB driver settings for v3.5.0 compatibility.
    /// </summary>
    private static void ConfigureGlobalMongoDriverSettings()
    {
        // Configure global conventions for MongoDB driver v3.5.0
        var conventionPack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new EnumRepresentationConvention(BsonType.String),
            new IgnoreExtraElementsConvention(true)
        };

        ConventionRegistry.Register(
            "KoanFrameworkGlobalConventions",
            conventionPack,
            t => true); // Apply to all types

        // Configure global GUID representation for v3.5.0 compatibility
        BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));
        BsonSerializer.RegisterSerializer(typeof(Guid?), new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));

    }

    /// <summary>
    /// Scans all loaded assemblies for Entity types that require GUID optimization.
    /// </summary>
    private static List<EntityOptimizationInfo> ScanForOptimizedEntityTypes()
    {

        var optimizedTypes = new List<EntityOptimizationInfo>();
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies()
            .Where(a => !a.IsDynamic && !IsSystemAssembly(a))
            .ToList();

        foreach (var assembly in assemblies)
        {
            try
            {
                var types = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract)
                    .ToList();

                foreach (var type in types)
                {
                    var optimizationInfo = AnalyzeEntityType(type);
                    if (optimizationInfo != null)
                    {
                        optimizedTypes.Add(optimizationInfo);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Error scanning assembly {assembly.FullName}: {ex.Message}");
            }
        }

        return optimizedTypes;
    }

    /// <summary>
    /// Analyzes a type to determine if it's an Entity type that requires optimization.
    /// </summary>
    private static EntityOptimizationInfo? AnalyzeEntityType(Type type)
    {
        // Look for IEntity<string> implementations
        var entityInterface = type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                               i.GetGenericTypeDefinition() == typeof(IEntity<>) &&
                               i.GetGenericArguments()[0] == typeof(string));

        if (entityInterface == null)
            return null;

        // Use direct reflection to check for Entity<T> pattern instead of framework's optimization detection
        // This duplicates the logic from StorageOptimizationExtensions but avoids service provider dependency

        // Check for explicit OptimizeStorageAttribute first
        var optimizeAttr = type.GetCustomAttribute<OptimizeStorageAttribute>(inherit: true);
        if (optimizeAttr != null)
        {
            if (optimizeAttr.OptimizationType == StorageOptimizationType.Guid)
            {
                return new EntityOptimizationInfo
                {
                    EntityType = type,
                    KeyType = typeof(string),
                    OptimizationType = StorageOptimizationType.Guid,
                    IdPropertyName = "Id", // Default property name
                    Reason = optimizeAttr.Reason
                };
            }
            return null; // Explicitly disabled or not GUID
        }

        // Check for Entity<T> pattern (should be optimized)
        var baseType = type.BaseType;
        while (baseType != null)
        {
            if (baseType.IsGenericType)
            {
                var genericTypeDef = baseType.GetGenericTypeDefinition();
                var genericArgs = baseType.GetGenericArguments();

                if (genericTypeDef.Name.StartsWith("Entity", StringComparison.Ordinal))
                {
                    // Entity<T> pattern (single generic - implicit string) - should optimize
                    if (genericArgs.Length == 1 && genericArgs[0] == type)
                    {
                        return new EntityOptimizationInfo
                        {
                            EntityType = type,
                            KeyType = typeof(string),
                            OptimizationType = StorageOptimizationType.Guid,
                            IdPropertyName = "Id", // Default property name
                            Reason = "Automatic GUID optimization for Entity<T> pattern (implicit string key)"
                        };
                    }

                    // Entity<T, string> pattern (explicit string) - don't optimize
                    if (genericArgs.Length == 2 &&
                        genericArgs[0] == type &&
                        genericArgs[1] == typeof(string))
                    {
                        return null;
                    }
                }
            }
            baseType = baseType.BaseType;
        }

        // Default: IEntity<string> implementations without Entity<T> pattern - don't optimize
        return null;
    }

    /// <summary>
    /// Registers global serializers for MongoDB driver v3.5.0 compatibility.
    /// Uses a global approach that overrides the default string serializer with smart GUID detection.
    /// </summary>
    private static void RegisterGlobalSerializers(List<EntityOptimizationInfo> optimizedTypes)
    {
        if (!optimizedTypes.Any())
        {
            return;
        }

        try
        {
            // For MongoDB driver v3.5.0, BsonClassMap is often ignored
            // Instead, register a global string serializer that automatically detects and optimizes GUIDs
            var smartGuidSerializer = new SmartStringGuidSerializer();

            // Override the default string serializer globally - this affects ALL string properties
            BsonSerializer.RegisterSerializer(typeof(string), smartGuidSerializer);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Falling back to per-class serializer registration: {ex.Message}");
            // Fallback to per-class approach
            RegisterPerClassSerializers(optimizedTypes);
        }
    }

    /// <summary>
    /// Fallback method using per-class BsonClassMap registration.
    /// </summary>
    private static void RegisterPerClassSerializers(List<EntityOptimizationInfo> optimizedTypes)
    {
        var smartGuidSerializer = new SmartStringGuidSerializer();

        foreach (var entityInfo in optimizedTypes)
        {
            try
            {
                if (!BsonClassMap.IsClassMapRegistered(entityInfo.EntityType))
                {
                    var classMap = new BsonClassMap(entityInfo.EntityType);
                    classMap.AutoMap();
                    classMap.SetIgnoreExtraElements(true);

                    var idProperty = entityInfo.EntityType.GetProperty(entityInfo.IdPropertyName);
                    if (idProperty != null)
                    {
                        var memberMap = classMap.GetMemberMap(entityInfo.IdPropertyName);
                        if (memberMap != null)
                        {
                            memberMap.SetSerializer(smartGuidSerializer);
                        }
                    }

                    BsonClassMap.RegisterClassMap(classMap);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Failed to register serializer for {entityInfo.EntityType.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Determines if an assembly is a system assembly that should be skipped.
    /// </summary>
    private static bool IsSystemAssembly(Assembly assembly)
    {
        var name = assembly.FullName ?? "";
        return name.StartsWith("System.") ||
               name.StartsWith("Microsoft.") ||
               name.StartsWith("mscorlib") ||
               name.StartsWith("netstandard") ||
               name.StartsWith("MongoDB.") ||
               name.Contains("Test");
    }
}

/// <summary>
/// Information about an entity type that requires optimization.
/// </summary>
public class EntityOptimizationInfo
{
    public required Type EntityType { get; set; }
    public required Type KeyType { get; set; }
    public required StorageOptimizationType OptimizationType { get; set; }
    public required string IdPropertyName { get; set; }
    public required string Reason { get; set; }
}

/// <summary>
/// Smart BSON serializer that handles string-to-GUID conversion for MongoDB driver v3.5.0.
/// Only converts strings that are valid GUIDs to BinData, leaves other strings as-is.
/// </summary>
public class SmartStringGuidSerializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();

        switch (bsonType)
        {
            case BsonType.Binary:
                var binaryData = context.Reader.ReadBinaryData();
                if (binaryData.SubType == BsonBinarySubType.UuidStandard)
                {
                    var guid = binaryData.ToGuid();
                    var result = guid.ToString("D");
                    return result;
                }
                break;

            case BsonType.String:
                var stringValue = context.Reader.ReadString();
                return stringValue;

            case BsonType.Null:
                context.Reader.ReadNull();
                return null!;
        }

        throw new FormatException($"Cannot convert {bsonType} to string GUID");
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
    {

        if (value == null)
        {
            context.Writer.WriteNull();
            return;
        }

        // Only convert to GUID BinData if the string is a valid GUID
        if (Guid.TryParse(value, out var guid))
        {
            // Store as native MongoDB UUID BinData for optimal performance and indexing
            var binaryData = new BsonBinaryData(guid, GuidRepresentation.Standard);
            context.Writer.WriteBinaryData(binaryData);
        }
        else
        {
            // Keep as string if not a valid GUID
            context.Writer.WriteString(value);
        }
    }
}