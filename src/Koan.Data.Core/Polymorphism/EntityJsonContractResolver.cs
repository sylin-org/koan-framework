using Koan.Data.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Koan.Data.Core.Polymorphism;

/// <summary>Adds Koan's serialize-only Entity-family type hint without changing domain models.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public class EntityJsonContractResolver : DefaultContractResolver
{
    protected override JsonObjectContract CreateObjectContract(Type objectType)
    {
        var contract = base.CreateObjectContract(objectType);
        if (!EntityRootDescriptor.TryFor(objectType, out _) ||
            contract.ExtensionDataGetter is not { } extensionData)
        {
            return contract;
        }

        contract.ExtensionDataGetter = value =>
            RejectReservedExtensionData(objectType, extensionData(value) ?? []);
        return contract;
    }

    protected override IList<JsonProperty> CreateProperties(
        Type type,
        MemberSerialization memberSerialization)
    {
        var properties = base.CreateProperties(type, memberSerialization);
        if (!EntityRootDescriptor.TryFor(type, out var descriptor))
        {
            return properties;
        }

        var reserved = properties.FirstOrDefault(static property =>
            string.Equals(
                property.PropertyName,
                EntityFamilyStorage.TypeField,
                StringComparison.OrdinalIgnoreCase));
        if (reserved is not null)
        {
            throw new InvalidOperationException(
                $"Entity '{type.FullName}' maps member '{reserved.UnderlyingName ?? reserved.PropertyName}' to reserved " +
                $"persistence field '{EntityFamilyStorage.TypeField}'. Rename or remap that member.");
        }

        if (!descriptor.IsVariant &&
            !EntityTypeCatalog.HasVariants(descriptor.RootType))
        {
            return properties;
        }

        properties.Add(new JsonProperty
        {
            PropertyName = EntityFamilyStorage.TypeField,
            UnderlyingName = EntityFamilyStorage.TypeField,
            PropertyType = typeof(string),
            DeclaringType = type,
            Readable = true,
            Writable = false,
            Order = int.MinValue,
            ValueProvider = RuntimeTypeValueProvider.Instance
        });

        return properties;
    }

    private static IEnumerable<KeyValuePair<object, object>> RejectReservedExtensionData(
        Type entityType,
        IEnumerable<KeyValuePair<object, object>> values)
    {
        foreach (var value in values)
        {
            if (string.Equals(
                    value.Key?.ToString(),
                    EntityFamilyStorage.TypeField,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.FullName}' contains extension data for reserved persistence field " +
                    $"'{EntityFamilyStorage.TypeField}'. Remove that key; Koan owns it for Entity-family identity.");
            }

            yield return value;
        }
    }

    private sealed class RuntimeTypeValueProvider : IValueProvider
    {
        public static RuntimeTypeValueProvider Instance { get; } = new();

        public object? GetValue(object target) => EntityTypeCatalog.TypeId(target.GetType());
        public void SetValue(object target, object? value) { }
    }
}
