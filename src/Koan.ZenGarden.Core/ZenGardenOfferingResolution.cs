namespace Koan.ZenGarden.Core;

/// <summary>
/// Resolved offering endpoint and metadata returned by Zen Garden initialization provider.
/// </summary>
public sealed record ZenGardenOfferingResolution
{
    /// <summary>
    /// Resolved tool fqid (for example: offering:mongodb or offering:ollama:dev).
    /// </summary>
    public required string ToolFqid { get; init; }

    /// <summary>
    /// Offering name.
    /// </summary>
    public required string Offering { get; init; }

    /// <summary>
    /// Optional offering instance name.
    /// </summary>
    public string? Instance { get; init; }

    /// <summary>
    /// Protocol declared in tool connection metadata.
    /// </summary>
    public string? Protocol { get; init; }

    /// <summary>
    /// Hostname declared in tool connection metadata.
    /// </summary>
    public string? Hostname { get; init; }

    /// <summary>
    /// IP declared in tool connection metadata.
    /// </summary>
    public string? Ip { get; init; }

    /// <summary>
    /// Port declared in tool connection metadata.
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// Candidate URIs emitted by tools projection.
    /// </summary>
    public IReadOnlyList<string> Uris { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Current offering capabilities map.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Capabilities { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Selects the best URI candidate by preferred scheme order, then falls back to host/port synthesis.
    /// </summary>
    public string? GetUri(params string[] preferredSchemes)
    {
        if (Uris.Count > 0)
        {
            if (preferredSchemes.Length > 0)
            {
                var preferred = preferredSchemes
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim().ToLowerInvariant())
                    .ToArray();

                foreach (var preferredScheme in preferred)
                {
                    foreach (var uri in Uris)
                    {
                        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                        {
                            continue;
                        }

                        if (string.Equals(parsed.Scheme, preferredScheme, StringComparison.OrdinalIgnoreCase))
                        {
                            return uri;
                        }
                    }
                }
            }

            foreach (var uri in Uris)
            {
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    return uri;
                }
            }
        }

        var host = !string.IsNullOrWhiteSpace(Hostname)
            ? Hostname
            : !string.IsNullOrWhiteSpace(Ip)
                ? Ip
                : null;
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var schemeCandidate = preferredSchemes.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        var scheme = !string.IsNullOrWhiteSpace(schemeCandidate)
            ? schemeCandidate.Trim().ToLowerInvariant()
            : !string.IsNullOrWhiteSpace(Protocol)
                ? Protocol.Trim().ToLowerInvariant()
                : "http";

        return Port is > 0
            ? $"{scheme}://{host}:{Port.Value}"
            : $"{scheme}://{host}";
    }
}
