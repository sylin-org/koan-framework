using System;

namespace Koan.Data.Vector;

/// <summary>
/// Exception thrown when entity and vector save operations fail to coordinate.
/// Indicates partial completion where entity may be saved but vector failed, or vice versa.
/// </summary>
public sealed class VectorCoordinationException : Exception
{
    /// <summary>
    /// The entity ID involved in the failed coordination.
    /// </summary>
    public object EntityId { get; }

    /// <summary>
    /// Whether the entity was successfully saved to the primary store.
    /// </summary>
    public bool EntitySaved { get; }

    /// <summary>
    /// Whether the vector was successfully saved to the vector store.
    /// </summary>
    public bool VectorSaved { get; }

    public VectorCoordinationException(
        string message,
        object entityId,
        bool entitySaved,
        bool vectorSaved,
        Exception? innerException = null)
        : base(message, innerException)
    {
        EntityId = entityId;
        EntitySaved = entitySaved;
        VectorSaved = vectorSaved;
    }
}
