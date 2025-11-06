namespace Koan.Data.AI.Attributes;

/// <summary>
/// Defines how embedding text is constructed from entity properties.
/// </summary>
public enum EmbeddingPolicy
{
    /// <summary>
    /// Only properties explicitly specified via Properties or Template are included.
    /// Requires Properties or Template to be set.
    /// </summary>
    Explicit,

    /// <summary>
    /// Automatically include all string and string[] properties (default).
    /// Use [EmbeddingIgnore] to exclude specific properties.
    /// </summary>
    AllStrings,

    /// <summary>
    /// Automatically include all public readable properties (advanced).
    /// Use [EmbeddingIgnore] to exclude specific properties.
    /// </summary>
    AllPublic
}
