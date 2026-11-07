using Koan.Data.Core.Commands;
using Koan.Web.Controllers;
using Koan.Web.Infrastructure;

namespace Koan.Web.Attributes;

/// <summary>
/// The partitions an <see cref="EntityController{TEntity,TKey}"/> serves to a caller who names one with <c>set</c> (WEB-0087). A controller
/// that declares none serves only the entity's own rows, and a request naming a partition there is refused with <c>set</c> named, because a
/// partition such as an audit trail holds what the entity's routes never show, and a write to another partition misses what watches the entity.
/// </summary>
/// <example><c>[Partitions("archive", "drafts")]</c> serves those two; <c>[Partitions(Any = true)]</c> serves whichever a caller names.</example>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class PartitionsAttribute(params string[] names) : Attribute
{
    /// <summary>The partitions served by name.</summary>
    public IReadOnlyList<string> Names { get; } = names;

    /// <summary>Whether every partition a caller names is served.</summary>
    public bool Any { get; set; }

    /// <summary>Whether <paramref name="partition"/> is served.</summary>
    public bool Serves(string partition) => Any || Names.Contains(partition, StringComparer.Ordinal);

    /// <summary>What a caller naming <paramref name="partition"/> through <paramref name="realization"/> is refused with, or null where it is
    /// served: a realization that is not an <see cref="EntityController{TEntity,TKey}"/> serves every partition, as it always has.</summary>
    public static Reason? Refusal(Type? realization, string? partition)
    {
        if (string.IsNullOrWhiteSpace(partition) || realization is null || !IsEntityController(realization)) return null;
        return EntityOperation.Of(null, realization).Partitions?.Serves(partition) == true
            ? null
            : Reason.Invalid(KoanWebConstants.Query.Set, $"This address serves no partition '{partition}'.");
    }

    private static bool IsEntityController(Type type)
    {
        for (var at = type; at is not null; at = at.BaseType)
            if (at.IsGenericType && at.GetGenericTypeDefinition() == typeof(EntityController<,>)) return true;
        return false;
    }
}
