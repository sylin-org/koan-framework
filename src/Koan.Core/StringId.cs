namespace Koan.Core;

/// <summary>
/// Minimal helpers for creating compact string identifiers.
/// Produces 32-char lowercase hex (UUID v7 "n" format) for time-ordered, globally unique IDs.
/// </summary>
public static class StringId
{
    /// <summary>Generates a new 32-char lowercase UUID v7 string (no dashes).</summary>
    public static string New() => Guid.CreateVersion7().ToString("n");

    /// <summary>Converts an existing Guid to 32-char lowercase hex.</summary>
    public static string From(Guid value) => value.ToString("n");
}
