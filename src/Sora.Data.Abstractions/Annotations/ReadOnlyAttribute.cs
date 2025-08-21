namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Marks an aggregate/entity as read-only for the data layer.
/// Adapters must not attempt schema changes when this attribute is present.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ReadOnlyAttribute : Attribute
{
}
