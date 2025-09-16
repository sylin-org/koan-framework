namespace Koan.Storage.Options;

public sealed class StorageOptions
{
    // Named profiles (profile -> provider+container)
    public Dictionary<string, StorageProfile> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    // Optional default profile name used when caller does not specify a profile
    public string? DefaultProfile { get; init; }

    // Fallback behavior when no profile is specified and DefaultProfile is not set
    public StorageFallbackMode FallbackMode { get; init; } = StorageFallbackMode.SingleProfileOnly;

    // If enabled, options validation will enforce basic invariants at resolution time
    public bool ValidateOnStart { get; init; } = true;

    public sealed class StorageProfile
    {
        public required string Provider { get; init; }
        public required string Container { get; init; }
    }
}
