using System.Reflection;
using System.Runtime.CompilerServices;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Data.Core.Polymorphism;

/// <summary>
/// Immutable, cached description of the physical Entity root owned by a CLR model type.
/// Infrastructure API for generated family companions and storage bridges (DATA-0109).
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class EntityRootDescriptor
{
    private static readonly ConditionalWeakTable<Type, EntityRootDescriptor> Cache = new();

    private EntityRootDescriptor(
        Type declaredType,
        Type rootType,
        Type keyType,
        Type? variantType)
    {
        DeclaredType = declaredType;
        RootType = rootType;
        KeyType = keyType;
        VariantType = variantType;
    }

    public Type DeclaredType { get; }
    public Type RootType { get; }
    public Type KeyType { get; }
    public Type? VariantType { get; }
    public bool IsVariant => VariantType is not null;
    public bool IsRoot => !IsVariant && DeclaredType == RootType;

    public static EntityRootDescriptor For(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return Cache.GetValue(entityType, Create);
    }

    public static bool TryFor(Type entityType, out EntityRootDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (!typeof(IEntity).IsAssignableFrom(entityType))
        {
            descriptor = null!;
            return false;
        }

        descriptor = For(entityType);
        return true;
    }

    private static EntityRootDescriptor Create(Type entityType)
    {
        if (!typeof(IEntity).IsAssignableFrom(entityType))
        {
            throw new InvalidOperationException(
                $"Type '{entityType.FullName}' is not a Koan Entity.");
        }

        var (rootType, keyType) = FindEntityRoot(entityType)
            ?? (entityType, FindEntityKey(entityType)
                ?? throw new InvalidOperationException(
                    $"Entity '{entityType.FullName}' does not expose one IEntity<TKey> contract."));

        if (!rootType.IsAssignableFrom(entityType))
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.FullName}' closes Entity<> over unrelated root '{rootType.FullName}'. " +
                $"The Entity root must be assignable from every stored family member.");
        }

        var marker = FindFamilyMarker(entityType, rootType, keyType);
        Type? variantType = null;

        if (entityType != rootType)
        {
            if (marker is null)
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.FullName}' inherits the Entity root '{rootType.FullName}' without closing " +
                    $"its generated family companion. Declare '{entityType.Name} : {rootType.Name}<{entityType.Name}>' " +
                    $"so point access stays typed and persistence remains rooted in '{rootType.Name}'.");
            }

            variantType = marker.GetGenericArguments()[1];
            if (variantType != entityType)
            {
                throw new InvalidOperationException(
                    $"Entity family member '{entityType.FullName}' closes '{rootType.Name}<>' over " +
                    $"'{variantType.FullName}', not itself. Declare '{entityType.Name} : {rootType.Name}<{entityType.Name}>'.");
            }
        }
        else if (marker is not null)
        {
            throw new InvalidOperationException(
                $"Entity root '{entityType.FullName}' cannot also be a family variant.");
        }

        ValidateProperties(entityType);
        return new EntityRootDescriptor(entityType, rootType, keyType, variantType);
    }

    private static (Type Root, Type Key)? FindEntityRoot(Type entityType)
    {
        for (var current = entityType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType ||
                current.GetGenericTypeDefinition() != typeof(Entity<,>))
            {
                continue;
            }

            var arguments = current.GetGenericArguments();
            return (arguments[0], arguments[1]);
        }

        return null;
    }

    private static Type? FindEntityKey(Type entityType)
        => entityType.GetInterfaces()
            .Where(static type => type.IsGenericType &&
                                  type.GetGenericTypeDefinition() == typeof(IEntity<>))
            .Select(static type => type.GetGenericArguments()[0])
            .Distinct()
            .SingleOrDefault();

    private static Type? FindFamilyMarker(Type entityType, Type rootType, Type keyType)
    {
        Type? marker = null;
        foreach (var contract in entityType.GetInterfaces())
        {
            if (!contract.IsGenericType ||
                contract.GetGenericTypeDefinition() != typeof(IEntityFamilyVariant<,,>))
            {
                continue;
            }

            var arguments = contract.GetGenericArguments();
            if (arguments[0] != rootType || arguments[2] != keyType)
            {
                continue;
            }

            if (marker is not null && marker != contract)
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.FullName}' declares more than one family-variant contract for root " +
                    $"'{rootType.FullName}'. Keep one self-closed family companion.");
            }

            marker = contract;
        }

        return marker;
    }

    private static void ValidateProperties(Type entityType)
    {
        var names = entityType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var collision = names
            .GroupBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.OrderBy(static name => name, StringComparer.Ordinal).ToArray())
            .FirstOrDefault(static group => group.Length > 1);

        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.FullName}' declares public properties whose names differ only by case: " +
                $"{string.Join(", ", collision.Select(static name => $"'{name}'"))}. " +
                "Rename one property so every persisted property has a unique case-insensitive name.");
        }

        if (names.Any(static name =>
                string.Equals(name, EntityFamilyStorage.TypeField, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Entity '{entityType.FullName}' declares reserved persistence property " +
                $"'{EntityFamilyStorage.TypeField}'. Rename it; Koan owns that field for Entity-family identity.");
        }
    }
}
