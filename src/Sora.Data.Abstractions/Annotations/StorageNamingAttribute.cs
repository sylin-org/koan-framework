using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Optional per-entity naming override to guide adapters when [Storage] is not set.
/// Explicit [Storage] still wins.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class StorageNamingAttribute : Attribute
{
    public StorageNamingStyle Style { get; }
    public StorageNamingAttribute(StorageNamingStyle style) => Style = style;
}
