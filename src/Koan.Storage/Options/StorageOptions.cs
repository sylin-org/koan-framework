using Koan.Storage.Replication;

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
        /// <summary>
        /// Provider name. Nullable — when absent, StorageService auto-detects
        /// from registered providers based on <see cref="Mode"/>.
        /// </summary>
        public string? Provider { get; init; }

        public required string Container { get; init; }

        /// <summary>
        /// Storage mode for this profile. Null = auto-detect from registered providers.
        /// </summary>
        public StorageMode? Mode { get; init; }

        /// <summary>
        /// Local cache configuration for replicated mode.
        /// Absent = unlimited cache (no eviction, full local mirror).
        /// </summary>
        public LocalCacheOptions? LocalCache { get; init; }

        /// <summary>
        /// When true, wraps the resolved provider with <see cref="ResilientStorageDecorator"/>
        /// for write-behind caching during primary unavailability.
        /// Legacy — superseded by <see cref="Mode"/> = <see cref="StorageMode.Replicated"/>.
        /// </summary>
        public bool Resilient { get; init; }
    }
}
