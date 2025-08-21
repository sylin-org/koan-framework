using System.Collections;

namespace Sora.Data.Abstractions;

/// <summary>
/// Lightweight shared type classification to help adapters decide storage mapping.
/// Adapters can use this to default complex types to JSON in relational providers.
/// </summary>
// ReSharper disable once InconsistentNaming
public static class TypeClassification
{
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
