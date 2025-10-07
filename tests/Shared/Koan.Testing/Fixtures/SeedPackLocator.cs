namespace Koan.Testing.Fixtures;

public static class SeedPackLocator
{
    public static string Resolve(string packId)
    {
        var segments = packId.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Pack identifier must not be empty.", nameof(packId));
        }

        var baseDirectory = DiscoverTestsRoot();
        var parts = new[] { baseDirectory, "SeedPacks" }.Concat(segments).ToArray();
        var candidate = Path.Combine(parts);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (!Path.HasExtension(candidate))
        {
            var withJson = candidate + ".json";
            if (File.Exists(withJson))
            {
                return withJson;
            }

            var defaultFile = Path.Combine(candidate, "default.json");
            if (File.Exists(defaultFile))
            {
                return defaultFile;
            }
        }

        throw new FileNotFoundException($"Seed pack '{packId}' not found.", candidate);
    }

    private static string DiscoverTestsRoot()
    {
        var candidates = new[]
        {
            new DirectoryInfo(AppContext.BaseDirectory),
            new DirectoryInfo(Directory.GetCurrentDirectory())
        };

        foreach (var start in candidates)
        {
            var match = TryFindTestsRoot(start);
            if (match is not null)
            {
                return match.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the tests root directory.");
    }

    private static DirectoryInfo? TryFindTestsRoot(DirectoryInfo start)
    {
        var directory = start;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "SeedPacks")))
            {
                return directory;
            }

            var nested = new DirectoryInfo(Path.Combine(directory.FullName, "tests"));
            if (nested.Exists && Directory.Exists(Path.Combine(nested.FullName, "SeedPacks")))
            {
                return nested;
            }

            directory = directory.Parent;
        }

        return null;
    }
}







