namespace Koan.Data.Abstractions;

/// <summary>A case-insensitive, immutable candidate settings snapshot supplied to one adapter probe.</summary>
public sealed class DataProviderProbeContext
{
    private readonly IReadOnlyDictionary<string, string?> _settings;

    public DataProviderProbeContext(IEnumerable<KeyValuePair<string, string?>> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Key))
                throw new ArgumentException("A candidate setting key cannot be empty.", nameof(settings));
            if (!snapshot.TryAdd(setting.Key.Trim(), setting.Value))
                throw new ArgumentException(
                    $"Candidate setting '{setting.Key.Trim()}' was supplied more than once.", nameof(settings));
        }
        _settings = snapshot;
    }

    public IReadOnlyDictionary<string, string?> Settings => _settings;

    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    public string Require(string key) => Get(key) ??
        throw new ArgumentException($"Candidate setting '{key}' is required.", nameof(key));

    public int GetInt32(string key, int fallback)
    {
        var value = Get(key);
        if (value is null) return fallback;
        if (int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        throw new ArgumentException($"Candidate setting '{key}' must be an integer.", nameof(key));
    }
}
