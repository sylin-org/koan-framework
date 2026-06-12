using System.Collections.Concurrent;
using Koan.Data.Core.Model;

namespace Koan.Data.Core;

/// <summary>
/// Guards against the entity-inheritance footgun: a concrete entity that inherits from another
/// concrete entity, for example <c>class Model2 : Model</c> where <c>Model : Entity&lt;Model&gt;</c>.
/// <para>
/// Koan binds an entity's set identity to the <c>Entity&lt;TEntity&gt;</c> root type. With concrete
/// inheritance the root type stays <c>Model</c>, so <c>new Model2().Save()</c> writes to the
/// <c>Model2</c> set (Save infers the compile-time type) while <c>Model2.Get(...)</c>, inherited from
/// <c>Entity&lt;Model&gt;</c>, reads the <c>Model</c> set. Writes and reads diverge silently, with no
/// error: the row persists but is unreadable through the type's own accessor.
/// </para>
/// <para>
/// The correct shape is for every entity to be its own root (<c>class Model2 : Entity&lt;Model2&gt;</c>)
/// and to share fields through a generic base (<c>abstract class YourBase&lt;T&gt; : Entity&lt;T&gt;</c>).
/// See the Entity how-to guide, "Sharing Shape Across Entities".
/// </para>
/// </summary>
public static class EntityShapeGuard
{
    private static readonly ConcurrentDictionary<Type, byte> Validated = new();

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if <paramref name="entityType"/> inherits from
    /// another concrete entity (its <c>Entity&lt;,&gt;</c> root type is not itself). Validation runs
    /// once per type and is cached; subsequent calls for a valid type are a single dictionary lookup.
    /// </summary>
    public static void EnsureOwnRoot(Type entityType)
    {
        if (Validated.ContainsKey(entityType)) return;

        var rootType = FindEntityRootArgument(entityType);
        if (rootType is not null && rootType != entityType)
        {
            throw new InvalidOperationException(BuildMessage(entityType, rootType));
        }

        Validated[entityType] = 1;
    }

    private static Type? FindEntityRootArgument(Type entityType)
    {
        for (var t = entityType.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Entity<,>))
            {
                return t.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static string BuildMessage(Type entityType, Type rootType)
        => $"Entity '{entityType.FullName}' inherits from another concrete entity '{rootType.FullName}'. " +
           $"Koan binds set identity to the Entity<> root type, so the set is '{rootType.Name}', not '{entityType.Name}'. " +
           $"Saving a '{entityType.Name}' writes to the '{entityType.Name}' set, but '{entityType.Name}.Get(...)' is inherited from " +
           $"Entity<{rootType.Name}> and reads the '{rootType.Name}' set: writes and reads diverge silently. " +
           $"Give '{entityType.Name}' its own root (class {entityType.Name} : Entity<{entityType.Name}>) and share fields through a " +
           $"generic base (abstract class YourBase<T> : Entity<T> where T : YourBase<T>). " +
           $"See the Entity how-to guide, 'Sharing Shape Across Entities'.";
}
