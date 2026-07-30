namespace Koan.Data.Abstractions;

public sealed record StorageContainerPage(
    IReadOnlyList<StorageContainerDescriptor> Containers,
    StorageContainerPageCompletion Completion,
    string? Continuation);
