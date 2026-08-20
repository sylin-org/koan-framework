using System.Collections;

namespace Koan.Data.Abstractions;

/// <summary>
/// Lightweight shared type classification to help adapters decide storage mapping.
/// Adapters can use this to default complex types to JSON in relational providers.
/// </summary>
// ReSharper disable once InconsistentNaming
public static class TypeClassification
{
    /// <summary>
    /// Whether a CLR value has a <i>proven</i> ordering across every qualified document and relational adapter,
    /// and may therefore carry a provider-paged stream.
    ///
    /// <para>The list is evidence, not taste. Each type here is ordered identically by every adapter and by the
    /// framework's own sorter, ascending and descending, under the cross-adapter oracle
    /// <c>SortPushdownConvergence</c>. Widening it means extending that corpus first and watching it pass on
    /// every store.</para>
    ///
    /// <para>What stays out, and why it is not timidity:</para>
    /// <list type="bullet">
    ///   <item><see cref="string"/> and <see cref="Guid"/> — collation, not values. The same two rows order
    ///   differently under a different database collation, and a stream would silently mean something else.</item>
    ///   <item><see cref="TimeSpan"/> — one adapter still stores it in .NET's default form and puts twenty-four
    ///   hours before twenty-three. Proven divergent rather than merely unproven.</item>
    ///   <item>Nullable values — provider null placement is not normalized for plain columns. A collection
    ///   aggregate is admitted despite being null for an empty collection, because its placement <i>is</i>
    ///   normalized where the dialect states it.</item>
    /// </list>
    ///
    /// <para>The Entity identifier is admitted separately by the stream coordinator as a provider-stable
    /// tie-break, not as a promise about CLR collation.</para>
    /// </summary>
    public static bool IsPortableStreamSortScalar(Type t)
    {
        if (t is null) return false;
        if (t.IsEnum) return true;
        return t == typeof(bool) ||
               t == typeof(byte) ||
               t == typeof(sbyte) ||
               t == typeof(short) ||
               t == typeof(ushort) ||
               t == typeof(int) ||
               t == typeof(long) ||
               t == typeof(decimal) ||
               t == typeof(double) ||
               t == typeof(DateTimeOffset) ||
               t == typeof(DateOnly) ||
               t == typeof(TimeOnly);
    }

    public static bool IsSimple(Type t)
    {
        if (t.IsPrimitive) return true;
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsEnum) return true;
        if (t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan) || t == typeof(Guid)) return true;
        if (t == typeof(byte[]) || t == typeof(ReadOnlyMemory<byte>) || t == typeof(Memory<byte>)) return true;
        return false;
    }

    public static bool IsCollection(Type t)
    {
        if (t == typeof(string)) return false;
        return typeof(IEnumerable).IsAssignableFrom(t);
    }

    public static bool IsComplex(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (IsSimple(t)) return false;
        // Treat collections as complex (JSON by default in relational)
        if (IsCollection(t)) return true;
        // Any class/record/struct that's not simple becomes complex.
        return t.IsClass || (t.IsValueType && !t.IsPrimitive && !t.IsEnum && t != typeof(decimal));
    }
}
