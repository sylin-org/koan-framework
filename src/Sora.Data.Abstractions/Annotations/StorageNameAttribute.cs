namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Overrides the storage name.
/// - On a property: sets the provider-level field/column name.
/// - On an entity type (class): acts as a shortcut to set the table/collection name.
/// Prefer to anchor indexes to the property rather than raw names.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class StorageNameAttribute : Attribute
{
    public StorageNameAttribute(string name) => Name = name;

    public string Name { get; }
}
