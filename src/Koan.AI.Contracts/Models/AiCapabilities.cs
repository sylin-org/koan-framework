using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

public record AiCapabilities
{
    public string AdapterId { get; init; } = string.Empty;
    public string AdapterType { get; init; } = string.Empty;
    public string? Version { get; init; }
    public bool SupportsChat { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool SupportsEmbeddings { get; init; }
    public AiModelManagementCapabilities? ModelManagement { get; init; }
}

public record AiModelManagementCapabilities
{
    public bool SupportsInstall { get; init; }
    public bool SupportsRemove { get; init; }
    public bool SupportsRefresh { get; init; }
    public bool SupportsProvenance { get; init; }
    public IReadOnlyList<string>? ProvisioningModes { get; init; }
    public IReadOnlyDictionary<string, string>? ProviderMetadata { get; init; }
}
