using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Data.Core.Polymorphism;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;

namespace Koan.Data.Connector.Mongo.Initialization;

/// <summary>
/// Thin BSON representation of DATA-0109's Koan-owned Entity type identity. It never resolves Mongo type names.
/// </summary>
internal sealed class MongoEntityDiscriminatorConvention : IDiscriminatorConvention
{
    private static readonly ConcurrentDictionary<Type, Lazy<bool>> Registrations = new();
    private static readonly ConcurrentDictionary<Type, byte> Validated = new();
    private static readonly ConcurrentDictionary<Type, BsonClassMap> ConfiguredFamilyMaps = new();
    private readonly Type _rootType;

    private MongoEntityDiscriminatorConvention(Type rootType) => _rootType = rootType;

    public string ElementName => EntityFamilyStorage.TypeField;

    public static void EnsureRegistered(Type rootType)
    {
        rootType = EntityRootDescriptor.For(rootType).RootType;

        var registration = Registrations.GetOrAdd(
            rootType,
            static type => new Lazy<bool>(
                () => Register(type),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            _ = registration.Value;
        }
        catch
        {
            Registrations.TryRemove(
                new KeyValuePair<Type, Lazy<bool>>(rootType, registration));
            throw;
        }
    }

    private static bool Register(Type rootType)
    {
        var convention = new MongoEntityDiscriminatorConvention(rootType);
        try
        {
            BsonSerializer.RegisterDiscriminatorConvention(
                rootType,
                convention);
        }
        catch (BsonSerializationException) when (
            BsonSerializer.LookupDiscriminatorConvention(rootType) is MongoEntityDiscriminatorConvention)
        {
            // A prior attempt completed the process-global convention step before a later class-map validation
            // failed. Registration is idempotent; continue so a safe retry reports or repairs the map step.
        }
        EnsureFamilyRootMap(rootType);
        ValidateReservedElement(rootType);
        return true;
    }

    internal static void ConfigureFamilyRootMap(BsonClassMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var descriptor = EntityRootDescriptor.For(map.ClassType);
        if (!descriptor.IsRoot ||
            !EntityTypeCatalog.HasVariants(descriptor.RootType))
        {
            return;
        }

        map.SetDiscriminatorConvention(
            new MongoEntityDiscriminatorConvention(descriptor.RootType));
        map.SetDiscriminatorIsRequired(true);
        map.SetIsRootClass(true);
        ConfiguredFamilyMaps[descriptor.RootType] = map;
    }

    private static void EnsureFamilyRootMap(Type rootType)
    {
        if (!EntityTypeCatalog.HasVariants(rootType))
        {
            return;
        }

        if (BsonClassMap.IsClassMapRegistered(rootType))
        {
            VerifyFamilyRootMap(BsonClassMap.LookupClassMap(rootType), rootType);
            return;
        }

        var map = new BsonClassMap(rootType);
        map.AutoMap();
        ConfigureFamilyRootMap(map);

        try
        {
            BsonClassMap.RegisterClassMap(map);
        }
        catch (ArgumentException) when (BsonClassMap.IsClassMapRegistered(rootType))
        {
            // A non-Koan serializer raced first. Validate the winning map below and fail with a correction if it
            // cannot guarantee a root identity.
        }

        VerifyFamilyRootMap(BsonClassMap.LookupClassMap(rootType), rootType);
    }

    private static void VerifyFamilyRootMap(BsonClassMap map, Type rootType)
    {
        if (!map.DiscriminatorIsRequired ||
            !ConfiguredFamilyMaps.TryGetValue(rootType, out var configured) ||
            !ReferenceEquals(configured, map))
        {
            throw new BsonSerializationException(
                $"MongoDB class mapping for Entity-family root '{rootType.FullName}' was frozen before Koan could " +
                $"require '{EntityFamilyStorage.TypeField}'. Configure custom BSON class maps before the first Data " +
                "operation, then let Koan register the family root.");
        }
    }

    public Type GetActualType(IBsonReader bsonReader, Type nominalType)
    {
        ValidateReservedElement(nominalType);
        var descriptor = EntityRootDescriptor.For(nominalType);
        var storedId = ReadStoredId(bsonReader);
        var explicitTarget = descriptor.IsVariant
            ? nominalType
            : EntityMaterializationScope.TargetFor(_rootType);
        var storedType = storedId is null ? null : EntityTypeCatalog.Resolve(_rootType, storedId);

        if (descriptor.IsVariant && storedType is not null && nominalType != storedType)
        {
            throw new BsonSerializationException(
                $"BSON Entity was declared as '{nominalType.FullName}' but storage identifies it as " +
                $"'{storedType.FullName}'.");
        }

        var actualType = storedType ?? explicitTarget ?? nominalType;
        if (!_rootType.IsAssignableFrom(actualType) ||
            descriptor.IsVariant && !nominalType.IsAssignableFrom(actualType))
        {
            throw new BsonSerializationException(
                $"Resolved BSON Entity type '{actualType.FullName}' does not belong to root '{_rootType.FullName}'.");
        }

        ValidateReservedElement(actualType);
        return actualType;
    }

    public BsonValue GetDiscriminator(Type nominalType, Type actualType)
    {
        ValidateReservedElement(nominalType);
        ValidateReservedElement(actualType);
        if (!_rootType.IsAssignableFrom(actualType))
        {
            throw new BsonSerializationException(
                $"Runtime type '{actualType.FullName}' does not belong to Entity root '{_rootType.FullName}'.");
        }

        return new BsonString(EntityTypeCatalog.TypeId(actualType));
    }

    private static void ValidateReservedElement(Type entityType)
    {
        if (Validated.ContainsKey(entityType))
        {
            return;
        }

        var collision = BsonClassMap.LookupClassMap(entityType)
            .AllMemberMaps
            .FirstOrDefault(static member =>
                string.Equals(
                    member.ElementName,
                    EntityFamilyStorage.TypeField,
                    StringComparison.OrdinalIgnoreCase));
        if (collision is not null)
        {
            throw new BsonSerializationException(
                $"Entity '{entityType.FullName}' maps member '{collision.MemberName}' to reserved BSON field " +
                $"'{EntityFamilyStorage.TypeField}'. Rename or remap that member.");
        }

        Validated[entityType] = 0;
    }

    private string? ReadStoredId(IBsonReader reader)
    {
        var bookmark = reader.GetBookmark();
        try
        {
            reader.ReadStartDocument();
            while (reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var name = reader.ReadName();
                if (!string.Equals(name, ElementName, StringComparison.Ordinal))
                {
                    reader.SkipValue();
                    continue;
                }

                if (reader.GetCurrentBsonType() != BsonType.String)
                {
                    throw new BsonSerializationException(
                        $"BSON Entity field '{ElementName}' must contain a string.");
                }

                return reader.ReadString();
            }

            return null;
        }
        finally
        {
            reader.ReturnToBookmark(bookmark);
        }
    }
}
