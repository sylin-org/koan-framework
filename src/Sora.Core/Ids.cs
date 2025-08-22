namespace Sora.Core;

/// <summary>
/// Minimal helpers for creating compact string identifiers.
/// Produces 32-char lowercase hex (Guid "n" format) for simplicity and portability.
/// </summary>
public static class StringId
{
    /// <summary>Generates a new 32-char lowercase GUID string (no dashes).</summary>
    public static string New() => Guid.NewGuid().ToString("n");

    /// <summary>Converts an existing Guid to 32-char lowercase hex.</summary>
    public static string From(Guid value) => value.ToString("n");
}
