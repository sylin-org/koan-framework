using Koan.Storage.Replication;

namespace Koan.Storage.Options;

public sealed class StorageOptions
{
    public Dictionary<string, StorageProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? DefaultProfile { get; set; }

    public sealed class StorageProfile
    {
        /// <summary>
        /// Provider name. Nullable — when absent, the compiled Storage routing plan elects
        /// from registered providers based on <see cref="Mode"/>.
        /// </summary>
        public string? Provider { get; set; }

        public string Container { get; set; } = "";

        /// <summary>
        /// Storage mode for this profile. Null = auto-detect from registered providers.
        /// </summary>
        public StorageMode? Mode { get; set; }

        /// <summary>
        /// Local cache configuration for replicated mode.
        /// Absent = unlimited cache (no eviction, full local mirror).
        /// </summary>
        public LocalCacheOptions? LocalCache { get; set; }
    }
}
