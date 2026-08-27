using System.Linq.Expressions;
using System.Reflection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Data.Hygiene;

/// <summary>
/// Scans an Entity type once for hygiene-annotated string properties and compiles in-place
/// normalizers: <c>[Trim]</c>, <c>[Lowercase]</c>, <c>[Uppercase]</c> (invariant culture).
/// Non-writable or non-string properties carrying hygiene attributes are skipped at scan time —
/// hygiene never throws, it just refuses to touch what it cannot normalize.
/// </summary>
internal sealed class HygienePropertyBag
{
    private readonly record struct Normalizer(PropertyInfo Property, Action<object> Apply);

    private readonly List<Normalizer> _normalizers = [];

    public HygienePropertyBag(Type entityType)
    {
        foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var hasTrim = property.IsDefined(typeof(TrimAttribute), inherit: true);
            var hasLower = property.IsDefined(typeof(LowercaseAttribute), inherit: true);
            var hasUpper = property.IsDefined(typeof(UppercaseAttribute), inherit: true);
            if (!hasTrim && !hasLower && !hasUpper) continue;
            if (property.PropertyType != typeof(string) || !property.CanRead || !property.CanWrite) continue;

            var setter = CompileSetter(property);
            _normalizers.Add(new Normalizer(property, entity => setter(entity, Normalize(property.GetValue(entity), hasTrim, hasLower, hasUpper))));
        }
    }

    public bool HasHygiene => _normalizers.Count > 0;

    public void Apply(object entity)
    {
        foreach (var normalizer in _normalizers)
            normalizer.Apply(entity);
    }

    private static object? Normalize(object? value, bool trim, bool lower, bool upper)
        => value is not string s ? value : HygieneTransform.Apply(s, trim, lower, upper);

    private static Action<object, object?> CompileSetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object), "instance");
        var value = Expression.Parameter(typeof(object), "value");
        var body = Expression.Assign(
            Expression.Property(Expression.Convert(instance, property.DeclaringType!), property),
            Expression.Convert(value, property.PropertyType));
        return Expression.Lambda<Action<object, object?>>(body, instance, value).Compile();
    }
}
