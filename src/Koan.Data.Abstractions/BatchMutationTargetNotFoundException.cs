namespace Koan.Data.Abstractions;

/// <summary>Thrown before native mutation when a deferred mutate-by-key target is absent or invisible.</summary>
public sealed class BatchMutationTargetNotFoundException : InvalidOperationException
{
    public BatchMutationTargetNotFoundException(string entityType, int operationIndex)
        : base(
            $"Deferred batch mutation {operationIndex} for '{entityType}' has no visible target. " +
            "Refresh the Entity or remove that mutation before saving the batch.")
    {
        EntityType = entityType;
        OperationIndex = operationIndex;
    }

    public string EntityType { get; }

    /// <summary>Zero-based position among the batch's deferred mutation operations.</summary>
    public int OperationIndex { get; }
}
