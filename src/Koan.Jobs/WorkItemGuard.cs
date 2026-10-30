using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Polymorphism;
using Newtonsoft.Json.Serialization;

namespace Koan.Jobs;

/// <summary>
/// The version a settle's conditional replace checks (JOBS-0010): the row as the settle just read it, member by member.
/// The members are the ones Koan persists — the same Json.NET contract persistence writes with, non-public setters
/// included. Each member is compared exactly or left out and named; nothing in between:
/// <list type="bullet">
/// <item>a scalar by value;</item>
/// <item>a set of scalars by its size and by holding each of them; a list only while it holds at most one scalar, since
/// Koan's filter language compares neither the order of a longer list nor its duplicates;</item>
/// <item>a nested object member by member, where the store pushes nested paths.</item>
/// </list>
/// Before anything is written, each member's comparison is checked against what the store can do: a comparison the store
/// would not push natively, a managed or classified field, a dictionary, a binary value, a null collection, a longer
/// list, a nested object on a store without nested paths, a string on a store whose string equality folds case, or a
/// member the store cannot resolve is left out and named, so one member never costs the whole guard and the caller can
/// say which writes go undetected.
/// </summary>
internal static class WorkItemGuard
{
    /// <summary>How deep into nested objects a guard follows members.</summary>
    private const int MaxDepth = 4;

    private static readonly EntityJsonContractResolver Contracts = new();
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<(JsonProperty Member, PropertyInfo? Property)>> Members = new();

    public static (Filter? Guard, IReadOnlyList<string> Unguarded) For(object stored, FilterSupport support)
    {
        var walk = new Walk(stored.GetType(), support, [], []);
        AddObject(walk, [], stored, depth: 0);
        var guard = walk.Clauses.Count switch
        {
            0 => null,
            1 => walk.Clauses[0],
            _ => Filter.All(walk.Clauses.ToArray()),
        };
        return (guard, walk.Unguarded);
    }

    private sealed record Walk(Type Root, FilterSupport Support, List<Filter> Clauses, List<string> Unguarded);

    private static void AddObject(Walk walk, string[] prefix, object value, int depth)
    {
        foreach (var (member, property) in MembersOf(value.GetType()))
        {
            if (prefix.Length == 0 && IsIdentityOrFamily(member)) continue;
            var path = prefix.Append(member.UnderlyingName!).ToArray();
            if (property is not { GetMethod.IsPublic: true } || property.IsDefined(typeof(ClassifiedAttribute), inherit: true))
            {
                walk.Unguarded.Add(string.Join('.', path));
                continue;
            }
            AddMember(walk, path, member.ValueProvider!.GetValue(value), property.PropertyType, depth);
        }
    }

    private static void AddMember(Walk walk, string[] path, object? value, Type declared, int depth)
    {
        var name = string.Join('.', path);
        if (path.Length > 1 && !walk.Support.NestedPaths)
        {
            walk.Unguarded.Add(name);
            return;
        }

        // The member's shape comes from its declared type, which is what the store's mapping binds; a member declared as
        // object or an interface takes the shape of the value it holds.
        var shape = declared == typeof(object) || declared.IsInterface && value is not null && !typeof(IEnumerable).IsAssignableFrom(declared)
            ? value?.GetType() ?? declared
            : declared;
        var field = FieldPath.Of(path);

        if (IsAtom(shape))
        {
            // A store whose string equality folds case would let a case-only change pass the guard.
            if (IsString(shape) && !walk.Support.OrdinalStrings)
            {
                walk.Unguarded.Add(name);
                return;
            }
            Admit(walk, name, Filter.On(field, FilterOperator.Eq, FilterValue.Of(value)));
            return;
        }

        if (shape == typeof(byte[]) || typeof(IDictionary).IsAssignableFrom(shape) || IsGenericDictionary(shape))
        {
            walk.Unguarded.Add(name);
            return;
        }

        if (typeof(IEnumerable).IsAssignableFrom(shape))
        {
            // A collection binds as one structured value. Its size and its elements compare exactly only while order and
            // duplicates cannot differ: an empty collection, a single scalar, or a set of scalars.
            if (value is not IEnumerable sequence)
            {
                walk.Unguarded.Add(name);
                return;
            }
            var items = sequence.Cast<object?>().ToList();
            var size = Filter.On(field, FilterOperator.Size, FilterValue.Of(items.Count));
            if (items.Count == 0)
            {
                Admit(walk, name, size);
                return;
            }
            var scalars = items.All(item => item is not null && IsAtom(item.GetType())
                                            && (walk.Support.OrdinalStrings || !IsString(item.GetType())));
            if (scalars && (items.Count == 1 || IsSet(shape, value)))
            {
                Admit(walk, name, size, Filter.On(field, FilterOperator.HasAll, FilterValue.Many(items.Distinct().ToList())));
                return;
            }
            walk.Unguarded.Add(name);
            return;
        }

        // A nested object compares member by member, only where the store pushes nested paths.
        if (!walk.Support.NestedPaths || depth + 1 >= MaxDepth)
        {
            walk.Unguarded.Add(name);
            return;
        }
        if (value is null)
        {
            Admit(walk, name, Filter.On(field, FilterOperator.Eq, FilterValue.Of(null)));
            return;
        }
        AddObject(walk, path, value, depth + 1);
    }

    /// <summary>Add a member's clauses to the guard only when the store compares every one of them natively, the same
    /// admission the conditional replace applies to the whole guard; otherwise name the member as left out.</summary>
    private static void Admit(Walk walk, string name, params Filter[] clauses)
    {
        foreach (var clause in clauses)
        {
            try
            {
                var frozen = Filter.Snapshot(clause, requireImmutableValues: true)!;
                if (FilterSplitter.Split(frozen, walk.Support, walk.Root).Residual is not null
                    || frozen is FieldFilter { Field: var path } && FieldPathResolver.Resolve(walk.Root, path).IsManaged)
                {
                    walk.Unguarded.Add(name);
                    return;
                }
            }
            catch (Exception refusal) when (refusal is NotSupportedException or InvalidFilterFieldException or MappingValueException
                                                or FormatException or InvalidCastException)
            {
                walk.Unguarded.Add(name);
                return;
            }
        }
        walk.Clauses.AddRange(clauses);
    }

    /// <summary>True when the member holds a set, whose elements carry neither order nor duplicates.</summary>
    private static bool IsSet(Type shape, object value)
        => IsSetType(shape) || IsSetType(value.GetType());

    private static bool IsSetType(Type type)
        => type.GetInterfaces().Append(type).Any(candidate => candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(ISet<>)
                || candidate.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)));

    private static bool IsGenericDictionary(Type type)
        => type.GetInterfaces().Append(type).Any(candidate => candidate.IsGenericType
            && (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));

    /// <summary>The members Koan persists for <paramref name="type"/>, with the CLR property behind each.</summary>
    private static IReadOnlyList<(JsonProperty Member, PropertyInfo? Property)> MembersOf(Type type)
        => Members.GetOrAdd(type, static t =>
        {
            if (Contracts.ResolveContract(t) is not JsonObjectContract contract) return [];
            return contract.Properties
                .Where(member => !member.Ignored && member.Readable && member.ValueProvider is not null
                                 && member.UnderlyingName is not null)
                .Select(member => (member, t.GetProperty(member.UnderlyingName!,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)))
                .ToList();
        });

    private static bool IsIdentityOrFamily(JsonProperty member)
        => string.Equals(member.UnderlyingName, nameof(IEntity<string>.Id), StringComparison.Ordinal)
           || string.Equals(member.PropertyName, EntityFamilyStorage.TypeField, StringComparison.Ordinal);

    private static bool IsString(Type type) => type == typeof(string) || type == typeof(char) || type == typeof(char?);

    /// <summary>A value the filter model carries as an immutable scalar atom.</summary>
    private static bool IsAtom(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr)
            || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(DateOnly) || type == typeof(TimeOnly);
    }
}
