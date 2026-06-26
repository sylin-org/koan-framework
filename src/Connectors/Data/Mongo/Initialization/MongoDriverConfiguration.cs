using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Data.Abstractions;
using Koan.Data.Core.Optimization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace Koan.Data.Connector.Mongo.Initialization;

/// <summary>
/// The single, once-guarded Mongo-family global driver configuration (ARCH-0103 §L). Folds the two
/// former static-config sites — <c>KoanAutoRegistrar.ConfigureMongoStaticState</c> and the
/// independently-discovered <c>MongoOptimizationAutoRegistrar</c> (with its separate lock and the
/// <c>Initialize(null!)</c> hand-invoke) — into ONE entry point behind ONE guard. The proven config
/// bodies (conventions, GUID/comparable serializers, per-member identity class maps) are preserved
/// verbatim; only the call structure changed.
/// <para>
/// This runs in the Initialize phase by necessity (ARCH-0086): the serializers must exist before any
/// Mongo operation during bootstrap — <c>KoanModule.Start</c> (host-start) is too late. <see cref="MongoModule"/>
/// invokes <see cref="EnsureApplied"/> from <c>Register</c>.
/// </para>
/// </summary>
internal static class MongoDriverConfiguration
{
    // CORE-0003: AppDomain-scoped guard for the process-global MongoDB driver state.
    private static readonly object _lock = new();
    private static bool _applied;

    /// <summary>Apply the global Mongo driver configuration exactly once per AppDomain.</summary>
    public static void EnsureApplied()
    {
        if (_applied)
        {
            return;
        }

        lock (_lock)
        {
            if (_applied)
            {
                return;
            }

            // Order preserved from the former two-site config: framework conventions (disable
            // discriminators / null-BsonValue / JObject provider) first, then the driver-global
            // GUID + comparable-encoding conventions, then the per-member identity class maps.
            ConfigureFrameworkConventions();
            ConfigureGlobalMongoDriverSettings();
            RegisterIdentitySerializers();

            _applied = true;
        }
    }

    /// <summary>
    /// Framework-level conventions: disable <c>_t</c>/<c>_v</c> discriminators, default null
    /// <see cref="BsonValue"/> members to <see cref="BsonNull"/>, and register the JObject provider.
    /// </summary>
    private static void ConfigureFrameworkConventions()
    {
        // Configure MongoDB conventions globally at startup - disables _t discriminators
        var pack = new ConventionPack
        {
            new IgnoreExtraElementsConvention(true),
            new NullBsonValueConvention()
        };

        try
        {
            ConventionRegistry.Register("KoanGlobalConventions", pack, _ => true);
        }
        catch (ArgumentException)
        {
            // Already registered - safe to ignore
        }

        // Disable discriminators by registering custom null discriminator convention
        // This prevents _t/_v fields from being added to documents
        try
        {
            BsonSerializer.RegisterDiscriminatorConvention(
                typeof(object),
                new NoDiscriminatorConvention());
        }
        catch (BsonSerializationException)
        {
            // Already registered - safe to ignore
        }

        try
        {
            BsonSerializer.RegisterSerializationProvider(new JObjectSerializationProvider());
        }
        catch (BsonSerializationException)
        {
            // Already registered - safe to ignore
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

        // Comparable-encoding contract (DATA-0100): a filterable/sortable scalar must persist in a
        // representation whose store-native ordering equals its CLR ordering. The driver's DEFAULT
        // DateTimeOffset encoding is a Document {DateTime, Ticks, Offset} — $lt/$gt on it is
        // lexicographic field-by-field, correct today only by the accident that DateTime is field 0;
        // and the default TimeSpan encoding is a String ("1.00:00:00") that does NOT sort by duration
        // (1-day sorts before 23h). Pin both to comparable primitives, mirroring the Guid block above:
        //   DateTimeOffset -> BsonType.DateTime  (the UTC instant; the offset is NOT persisted)
        //   TimeSpan       -> BsonType.Int64     (ticks)
        // DateOnly/TimeOnly already default to comparable primitives (DateTime / Int64) and need no
        // registration. Each registration is guarded INDIVIDUALLY: if one type is already registered by a
        // third party (throws BsonSerializationException), the others must still be applied — a single
        // shared try/catch would silently leave e.g. TimeSpan on the broken string encoding.
        TryRegister(typeof(DateTimeOffset), new DateTimeOffsetSerializer(BsonType.DateTime));
        TryRegister(typeof(DateTimeOffset?), new NullableSerializer<DateTimeOffset>(new DateTimeOffsetSerializer(BsonType.DateTime)));
        TryRegister(typeof(TimeSpan), new TimeSpanSerializer(BsonType.Int64));
        TryRegister(typeof(TimeSpan?), new NullableSerializer<TimeSpan>(new TimeSpanSerializer(BsonType.Int64)));
    }

    /// <summary>Register a serializer, tolerating the "already registered" case per-type so one failure
    /// cannot skip the remaining registrations (comparable-encoding contract, DATA-0100).</summary>
    private static void TryRegister(Type type, IBsonSerializer serializer)
    {
        try { BsonSerializer.RegisterSerializer(type, serializer); }
        catch (BsonSerializationException) { /* already registered - safe to ignore */ }
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
            catch
            {
                // One entity's class-map registration must never break the global BSON init.
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
            catch
            {
                continue;   // a non-loadable assembly must never break the entity scan
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
/// Custom discriminator convention that disables discriminator serialization entirely.
/// This prevents MongoDB from adding _t fields to documents.
/// </summary>
public class NoDiscriminatorConvention : IDiscriminatorConvention
{
    public string ElementName => "_t";
    public Type GetActualType(MongoDB.Bson.IO.IBsonReader bsonReader, Type nominalType) => nominalType;
    public MongoDB.Bson.BsonValue GetDiscriminator(Type nominalType, Type actualType) => null!;
}

/// <summary>
/// Convention to handle nulls for BsonValue properties globally.
/// </summary>
public class NullBsonValueConvention : IMemberMapConvention
{
    public string Name => "NullBsonValueConvention";
    public void Apply(BsonMemberMap memberMap)
    {
        if (memberMap.MemberType == typeof(BsonValue))
        {
            memberMap.SetDefaultValue(BsonNull.Value);
        }
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
