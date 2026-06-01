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

namespace Koan.Data.Connector.Mongo.Initialization;

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
                // Step 1: global driver conventions + Guid/Guid? representation.
                ConfigureGlobalMongoDriverSettings();

                // Step 2 (DATA-0098): register the GUID identity serializer per-member, on declared
                // identity fields only — there is NO global typeof(string) override.
                RegisterIdentitySerializers();

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
            new IgnoreExtraElementsConvention(true),
            // GUID carve-out (DATA-XXXX): force List<string>/string[] ELEMENTS to serialize as BSON
            // strings so the global SmartStringGuidSerializer can't rewrite Guid-shaped elements to
            // BinData (which would corrupt List<string> round-trips and break array containment).
            new StringCollectionElementConvention()
        };

        try
        {
            ConventionRegistry.Register(
                "KoanFrameworkGlobalConventions",
                conventionPack,
                t => true); // Apply to all types
        }
        catch (ArgumentException)
        {
            // Already registered - safe to ignore
        }

        // Configure global GUID representation for v3.5.0 compatibility
        try
        {
            BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));
            BsonSerializer.RegisterSerializer(typeof(Guid?), new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));
        }
        catch (BsonSerializationException)
        {
            // Already registered - safe to ignore
        }
    }

    /// <summary>
    /// Registers the GUID identity serializer on exactly the GUID-encoded members of each entity —
    /// the Id of an <c>Entity&lt;T&gt;</c> and parent references to GUID-identity entities, per
    /// <see cref="IdentityEncoding"/> — via per-type <see cref="BsonClassMap"/>. DATA-0098: there is
    /// NO global <c>typeof(string)</c> override, so every other string keeps the default serializer,
    /// encoding is scoped to declared identity (no over-reach), and it cannot drift between the write
    /// and query paths (both consult <see cref="IdentityEncoding"/>).
    /// </summary>
    private static void RegisterIdentitySerializers()
    {
        var identitySerializer = new SmartStringGuidSerializer();

        foreach (var entityType in ScanEntityTypes())
        {
            IReadOnlySet<string> members;
            try
            {
                members = IdentityEncoding.GuidEncodedMembers(entityType);
            }
            catch
            {
                continue; // one entity's metadata must never break global init
            }

            if (members.Count == 0 || BsonClassMap.IsClassMapRegistered(entityType))
            {
                continue;
            }

            try
            {
                var classMap = new BsonClassMap(entityType);
                classMap.AutoMap();
                classMap.SetIgnoreExtraElements(true);

                foreach (var member in members)
                {
                    classMap.GetMemberMap(member)?.SetSerializer(identitySerializer);
                }

                BsonClassMap.RegisterClassMap(classMap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Failed to register identity serializers for {entityType.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Concrete, non-abstract <see cref="IEntity{TKey}"/> types from the cached assemblies.</summary>
    private static IEnumerable<Type> ScanEntityTypes()
    {
        var assemblies = AssemblyCache.Instance.GetAllAssemblies()
            .Where(a => !a.IsDynamic && !IsSystemAssembly(a));

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MONGO-AUTO-REGISTRAR] Error scanning assembly {assembly.FullName}: {ex.Message}");
                continue;
            }

            foreach (var type in types)
            {
                if (type.IsClass && !type.IsAbstract && ImplementsEntity(type))
                {
                    yield return type;
                }
            }
        }
    }

    private static bool ImplementsEntity(Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEntity<>))
            {
                return true;
            }
        }

        return false;
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

        // Single source of truth shared with MongoFilterTranslator so the write and query paths
        // can never drift (DATA-XXXX): a Guid-parseable string is persisted as native UUID BinData.
        if (MongoGuidEncoding.IsGuidEncoded(value, out var guid))
        {
            context.Writer.WriteBinaryData(MongoGuidEncoding.ToBinData(guid));
        }
        else
        {
            context.Writer.WriteString(value);
        }
    }
}
