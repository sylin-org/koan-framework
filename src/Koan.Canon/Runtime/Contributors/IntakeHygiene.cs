using System.Reflection;
using Koan.Data.Abstractions.Annotations;

namespace Koan.Canon.Internal;

/// <summary>
/// Per-model sweep over the shared hygiene annotations (<see cref="TrimAttribute"/>, <see cref="LowercaseAttribute"/>,
/// <see cref="UppercaseAttribute"/>) applied at canon intake, so match keys and validators always see prepared values
/// even when the model carries no manual <see cref="CanonEntity{TModel}.OnIntake"/> normalization. The meaning of the
/// attributes is owned by <see cref="HygieneTransform"/> in Data.Abstractions — the identical semantics the persistence
/// tier applies at save — so intake and stored rows can never disagree on an identity token's shape.
///
/// <para>Scanned once per closed model type. Property selection mirrors the persistence sweep exactly: non-string,
/// non-readable, or non-writable properties carrying a hygiene annotation are skipped — hygiene never throws, it
/// refuses to touch what it cannot normalize.</para>
/// </summary>
internal static class IntakeHygiene<TModel> where TModel : class
{
    private readonly record struct Sweep(PropertyInfo Property, bool Trim, bool Lower, bool Upper);

    private static readonly List<Sweep> Sweeps = Scan();

    private static List<Sweep> Scan()
    {
        var sweeps = new List<Sweep>();
        foreach (var property in typeof(TModel).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var trim = property.IsDefined(typeof(TrimAttribute), inherit: true);
            var lower = property.IsDefined(typeof(LowercaseAttribute), inherit: true);
            var upper = property.IsDefined(typeof(UppercaseAttribute), inherit: true);
            if (!trim && !lower && !upper) continue;
            if (property.PropertyType != typeof(string) || !property.CanRead || !property.CanWrite) continue;
            sweeps.Add(new Sweep(property, trim, lower, upper));
        }
        return sweeps;
    }

    /// <summary>Apply every annotated property's declared normalization in place.</summary>
    public static void Apply(TModel candidate)
    {
        foreach (var sweep in Sweeps)
        {
            if (sweep.Property.GetValue(candidate) is not string value) continue;
            sweep.Property.SetValue(candidate, HygieneTransform.Apply(value, sweep.Trim, sweep.Lower, sweep.Upper));
        }
    }
}
