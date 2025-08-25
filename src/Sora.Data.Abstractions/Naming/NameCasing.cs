namespace Sora.Data.Abstractions.Naming;

/// <summary>
/// Casing transforms for derived storage names.
/// </summary>
public enum NameCasing
{
    AsIs = 0,
    Lower,
    Upper,
    Pascal,
    Camel,
    Snake,
    Kebab
}