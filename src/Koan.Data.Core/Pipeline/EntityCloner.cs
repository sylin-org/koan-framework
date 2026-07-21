using System.Reflection;

namespace Koan.Data.Core.Pipeline;

/// <summary>
/// A generic shallow clone for the clone-then-transform write path (ARCH-0098 §0). When an entity type has a
/// field transform, the facade persists a clone (encrypted) so the caller's instance keeps its plaintext. A
/// shallow clone is sufficient: a transform reassigns the (typically immutable string) protected properties on the
/// clone, which never touches the caller's original; unprotected reference members are shared but never mutated.
/// </summary>
internal static class EntityCloner
{
    // object.MemberwiseClone is a protected instance method; bound as an open delegate it becomes a universal
    // shallow-cloner (this -> copy) usable for any reference type, with no per-type reflection on the hot path.
    private static readonly Func<object, object> Clone =
        (Func<object, object>)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<object, object>));

    /// <summary>Returns a shallow copy of <paramref name="entity"/>.</summary>
    public static object ShallowClone(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Clone(entity);
    }
}
