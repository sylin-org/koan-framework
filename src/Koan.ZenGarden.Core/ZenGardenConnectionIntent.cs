using System.Collections.ObjectModel;

namespace Koan.ZenGarden.Core;

/// <summary>
/// Parsed representation of a Zen Garden connection intent URI.
/// </summary>
public sealed record ZenGardenConnectionIntent
{
    /// <summary>
    /// Canonical URI scheme for Zen Garden connection intents.
    /// </summary>
    public const string Scheme = "zen-garden";

    /// <summary>
    /// Offering name (for example: mongodb, ollama).
    /// </summary>
    public required string Offering { get; init; }

    /// <summary>
    /// Optional offering instance selector (for example: dev).
    /// </summary>
    public string? Instance { get; init; }

    /// <summary>
    /// Optional required capabilities (bare by default, typed optional).
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates an intent from offering metadata.
    /// </summary>
    public static ZenGardenConnectionIntent ForOffering(
        string offering,
        string? instance = null,
        IEnumerable<string>? capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(offering))
        {
            throw new ArgumentException("Offering is required.", nameof(offering));
        }

        return new ZenGardenConnectionIntent
        {
            Offering = NormalizeIdentifier(offering),
            Instance = string.IsNullOrWhiteSpace(instance) ? null : NormalizeIdentifier(instance),
            Capabilities = NormalizeCapabilities(capabilities)
        };
    }

    public static bool TryParse(string? raw, out ZenGardenConnectionIntent? intent)
    {
        intent = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var value = raw.Trim();
        var prefix = $"{Scheme}://";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = value[prefix.Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var queryIndex = payload.IndexOf('?', StringComparison.Ordinal);
        var target = queryIndex >= 0 ? payload[..queryIndex] : payload;
        var query = queryIndex >= 0 ? payload[(queryIndex + 1)..] : string.Empty;

        target = target.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        var slashIndex = target.IndexOf('/');
        if (slashIndex >= 0)
        {
            target = target[..slashIndex];
        }

        var instanceIndex = target.IndexOf(':', StringComparison.Ordinal);
        var offering = instanceIndex >= 0 ? target[..instanceIndex] : target;
        var instance = instanceIndex >= 0 ? target[(instanceIndex + 1)..] : null;

        if (string.IsNullOrWhiteSpace(offering))
        {
            return false;
        }

        if (instanceIndex >= 0 && string.IsNullOrWhiteSpace(instance))
        {
            return false;
        }

        intent = new ZenGardenConnectionIntent
        {
            Offering = NormalizeIdentifier(Uri.UnescapeDataString(offering)),
            Instance = string.IsNullOrWhiteSpace(instance)
                ? null
                : NormalizeIdentifier(Uri.UnescapeDataString(instance)),
            Capabilities = ParseCapabilitiesFromQuery(query)
        };

        return true;
    }

    /// <summary>
    /// Converts intent into offering selector form used by tools APIs.
    /// </summary>
    public string ToOfferingSelector()
    {
        return string.IsNullOrWhiteSpace(Instance)
            ? Offering
            : $"{Offering}:{Instance}";
    }

    private static IReadOnlyList<string> ParseCapabilitiesFromQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator >= 0 ? pair[..separator] : pair;
            if (!key.Equals("cap", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator >= 0 ? pair[(separator + 1)..] : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            values.Add(Uri.UnescapeDataString(value));
        }

        return NormalizeCapabilities(values);
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string>? raw)
    {
        if (raw is null)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            foreach (var token in entry.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var capability = token.Trim().ToLowerInvariant();
                if (seen.Add(capability))
                {
                    normalized.Add(capability);
                }
            }
        }

        return new ReadOnlyCollection<string>(normalized);
    }

    private static string NormalizeIdentifier(string raw)
    {
        return raw.Trim().ToLowerInvariant();
    }
}
