namespace Sora.Data.Abstractions.Annotations;

// Back-compat shim for older code; prefer ReadOnlyAttribute
// Kept to avoid breaking existing consumers. This attribute has the same intent
// but new code should use Sora.Data.Abstractions.Annotations.ReadOnlyAttribute.
[Obsolete("Use Sora.Data.Abstractions.Annotations.ReadOnlyAttribute instead.")]
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ReadOnlyEntityAttribute : Attribute
{
}
