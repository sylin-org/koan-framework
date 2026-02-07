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

    private static bool IsContainerized()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
