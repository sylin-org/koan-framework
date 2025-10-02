namespace S5.Recs.Services;

/// <summary>
/// Registry for media parsers mapped by source code.
/// Enables rebuild-from-cache to select the correct parser based on cache location.
/// </summary>
public interface IMediaParserRegistry
{
    /// <summary>
    /// Get parser for a specific source code (e.g., "anilist", "myanimelist")
    /// </summary>
    IMediaParser? GetParser(string sourceCode);
}

internal sealed class MediaParserRegistry : IMediaParserRegistry
{
    private readonly Dictionary<string, IMediaParser> _parsers;

    public MediaParserRegistry(IEnumerable<IMediaParser> parsers)
    {
        _parsers = parsers.ToDictionary(p => p.SourceCode, StringComparer.OrdinalIgnoreCase);
    }

    public IMediaParser? GetParser(string sourceCode)
    {
        return _parsers.TryGetValue(sourceCode, out var parser) ? parser : null;
    }
}
