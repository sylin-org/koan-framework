namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Describes lineage mutations tracked for canonical entities.
/// </summary>
public enum CanonLineageChangeKind
{
    /// <summary>
    /// A parent relationship was established.
    /// </summary>
    ParentLinked = 0,

    /// <summary>
    /// A parent relationship was removed.
    /// </summary>
    ParentUnlinked = 1,

    /// <summary>
    /// A child relationship was created.
    /// </summary>
    ChildLinked = 2,

    /// <summary>
    /// A child relationship was removed.
    /// </summary>
    ChildUnlinked = 3,

    /// <summary>
    /// The canonical entity was superseded by another identifier.
    /// </summary>
    SupersededBy = 4,

    /// <summary>
    /// This canonical entity superseded another identifier.
    /// </summary>
    Superseded = 5,

    /// <summary>
    /// Lineage metadata was updated without structural change.
    /// </summary>
    MetadataUpdated = 6
}
