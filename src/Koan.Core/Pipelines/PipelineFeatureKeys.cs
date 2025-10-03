namespace Koan.Core.Pipelines;

/// <summary>
/// Common feature keys stored on <see cref="PipelineEnvelope{TEntity}"/> instances.
/// </summary>
public static class PipelineFeatureKeys
{
    public const string Embedding = "embedding";
    public const string EmbeddingModel = "embedding:model";
    public const string Notification = "notification";
}
