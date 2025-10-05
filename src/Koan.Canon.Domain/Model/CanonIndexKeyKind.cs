namespace Koan.Canon.Domain.Model;

/// <summary>
/// Describes the category of key represented within the shared canon index.
/// </summary>
public enum CanonIndexKeyKind
{
    /// <summary>
    /// Aggregation key computed for the canonical entity.
    /// </summary>
    Aggregation = 0,

    /// <summary>
    /// External identifier provided by a source system.
    /// </summary>
    ExternalId = 1,

    /// <summary>
    /// Parent-child relationship key.
    /// </summary>
    Parent = 2,

    /// <summary>
    /// Custom key authored by a pipeline extension.
    /// </summary>
    Custom = 3
}
