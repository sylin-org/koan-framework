namespace Koan.Data.Abstractions;

/// <summary>Adapter result before Data wraps the provider continuation in a source-bound envelope.</summary>
public sealed record SourceContainerBatch(
    IReadOnlyList<StorageContainerDescriptor> Containers,
    StorageContainerPageCompletion Completion,
    string? ProviderContinuation);
