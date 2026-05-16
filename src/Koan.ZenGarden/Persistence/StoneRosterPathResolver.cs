namespace Koan.ZenGarden.Persistence;

/// <summary>
/// Resolves the filesystem path for the Stone roster cache file.
/// </summary>
internal static class StoneRosterPathResolver
{
    private const string ContainerCacheRoot = "/app/cache";

    /// <summary>
    /// Resolution chain:
    /// 1. Explicit option <see cref="ZenGardenOptions.DiscoveryCachePath"/>
    /// 2. <c>KOAN_ZENGARDEN_CACHE_PATH</c> environment variable
    /// 3. Container convention: <c>/app/cache/zen-garden/</c> when <c>DOTNET_RUNNING_IN_CONTAINER=true</c>
    ///    and the standard <c>/app/cache</c> volume mount exists
    /// 4. <c>.Koan/zen-garden/</c> relative to current directory (host convention)
    /// </summary>
    public static string Resolve(ZenGardenOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DiscoveryCachePath))
        {
            return Path.Combine(
                options.DiscoveryCachePath.Trim(),
                Constants.Persistence.RosterFileName);
        }

        var envPath = Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.CachePath);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.Combine(envPath.Trim(), Constants.Persistence.RosterFileName);
        }

        // In a container, prefer the standard /app/cache volume mount so the roster
        // survives container restarts without requiring explicit configuration.
        if (IsContainerized() && Directory.Exists(ContainerCacheRoot))
        {
            return Path.Combine(ContainerCacheRoot, "zen-garden", Constants.Persistence.RosterFileName);
        }

        var conventionDir = Path.Combine(
            Directory.GetCurrentDirectory(),
            Constants.Persistence.DefaultCacheSubdirectory);

        return Path.Combine(conventionDir, Constants.Persistence.RosterFileName);
    }

    /// <summary>
    /// Resolves the path for the Moss-authored topology file.
    /// Resolution chain:
    /// 1. Same directory as roster (container mount or explicit config — co-located files)
    /// 2. <c>GARDEN_DATA_DIR</c> env var + <c>/topology/garden-topology.json</c>
    /// 3. System-wide Zen Garden data directory:
    ///    - Linux: <c>/var/lib/zen-garden/topology/</c>
    ///    - Windows: <c>%ProgramData%\zen-garden\topology\</c>
    /// Returns the first path where the file actually exists, or the roster-adjacent
    /// path as a default (so the file can appear there later).
    /// </summary>
    public static string? ResolveMossTopologyPath(ZenGardenOptions options)
    {
        var rosterPath = Resolve(options);
        var rosterDir = Path.GetDirectoryName(rosterPath);

        // 1. Co-located with roster (container mount or explicit config)
        if (!string.IsNullOrEmpty(rosterDir))
        {
            var colocated = Path.Combine(rosterDir, Constants.Persistence.MossTopologyFileName);
            if (File.Exists(colocated))
                return colocated;
        }

        // 2. GARDEN_DATA_DIR env var (Moss convention, overridable)
        var gardenDataDir = Environment.GetEnvironmentVariable("GARDEN_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(gardenDataDir))
        {
            var envPath = Path.Combine(gardenDataDir.Trim(), "topology", Constants.Persistence.MossTopologyFileName);
            if (File.Exists(envPath))
                return envPath;
        }

        // 3. System-wide Zen Garden data directory (platform-specific)
        var systemPath = ResolveSystemTopologyPath();
        if (systemPath is not null && File.Exists(systemPath))
            return systemPath;

        // Default: roster-adjacent path (file may appear later via mount injection)
        return string.IsNullOrEmpty(rosterDir)
            ? null
            : Path.Combine(rosterDir, Constants.Persistence.MossTopologyFileName);
    }

    private static string? ResolveSystemTopologyPath()
    {
        // System-wide stable paths that survive user changes and are accessible by services.
        // These match Moss's data_dir conventions from paths.rs / filesystem.rs.
        if (OperatingSystem.IsLinux())
        {
            return Path.Combine("/var/lib/zen-garden", "topology", Constants.Persistence.MossTopologyFileName);
        }

        if (OperatingSystem.IsWindows())
        {
            // C:\ProgramData\zen-garden — same as Moss's Windows service installation path
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrEmpty(programData))
                return Path.Combine(programData, "zen-garden", "topology", Constants.Persistence.MossTopologyFileName);
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine("/var/lib/zen-garden", "topology", Constants.Persistence.MossTopologyFileName);
        }

        return null;
    }

    private static bool IsContainerized()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
