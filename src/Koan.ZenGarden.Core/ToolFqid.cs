namespace Koan.ZenGarden.Core;

/// <summary>
/// Immutable identifier for a Zen Garden tool (offering or seed-bank).
/// Format: "offering_type" (bare) or "offering_type:instance" (qualified).
/// </summary>
/// <remarks>
/// Port of the Rust <c>OfferingFqn</c> / <c>fqid_matches</c> logic from zen-garden common crate.
/// Centralizes fqid parsing, normalization, and matching that was previously
/// scattered across ZenGardenSubscription, ZenGardenClient, and ZenGardenInitializationProvider.
/// </remarks>
public readonly record struct ToolFqid
{
    /// <summary>Instance separator (canonical).</summary>
    public const char Separator = ':';

    /// <summary>Double-colon separator used on the wire by Moss (e.g., "ollama::orchestrator").</summary>
    private const string DoubleSeparator = "::";

    /// <summary>Legacy instance separator (backward compat).</summary>
    private const char LegacySeparator = '@';

    /// <summary>Offering type (e.g., "mongodb", "ollama"). Always lowercase.</summary>
    public string OfferingType { get; }

    /// <summary>Optional instance name (e.g., "prod", "adopted"). Always lowercase, null when default instance.</summary>
    public string? Instance { get; }

    private ToolFqid(string offeringType, string? instance)
    {
        OfferingType = offeringType;
        Instance = instance;
    }

    /// <summary>
    /// Parse a raw fqid string into a <see cref="ToolFqid"/>.
    /// Handles normalization: lowercase, strips legacy prefixes ("offering:", "seed-bank:"),
    /// normalizes '@' separator to ':'.
    /// </summary>
    public static ToolFqid Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("Tool fqid cannot be empty.", nameof(raw));

        var normalized = StripLegacyPrefix(raw.Trim()).ToLowerInvariant();

        // Handle wire-format double-colon separator first (e.g., "ollama::orchestrator")
        var doubleIndex = normalized.IndexOf(DoubleSeparator, StringComparison.Ordinal);
        if (doubleIndex > 0)
        {
            var offering = normalized[..doubleIndex];
            var instance = normalized[(doubleIndex + DoubleSeparator.Length)..];

            if (string.IsNullOrEmpty(instance))
                return new ToolFqid(offering, null);

            if (string.Equals(offering, instance, StringComparison.Ordinal))
                return new ToolFqid(offering, null);

            return new ToolFqid(offering, instance);
        }

        var separatorIndex = normalized.IndexOf(Separator);
        if (separatorIndex < 0)
            separatorIndex = normalized.IndexOf(LegacySeparator);

        if (separatorIndex > 0 && separatorIndex < normalized.Length - 1)
        {
            var offering = normalized[..separatorIndex];
            var instance = normalized[(separatorIndex + 1)..];

            // Same-name instance collapses to default (matches Rust behavior)
            if (string.Equals(offering, instance, StringComparison.Ordinal))
                return new ToolFqid(offering, null);

            return new ToolFqid(offering, instance);
        }

        // Trailing separator or no separator — bare name
        var bareName = separatorIndex >= 0 ? normalized[..separatorIndex] : normalized;
        return new ToolFqid(bareName, null);
    }

    /// <summary>Try-parse variant that returns false on invalid input.</summary>
    public static bool TryParse(string? raw, out ToolFqid result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        try
        {
            result = Parse(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Create a <see cref="ToolFqid"/> from already-decomposed parts.
    /// </summary>
    public static ToolFqid From(string offeringType, string? instance = null)
    {
        if (string.IsNullOrWhiteSpace(offeringType))
            throw new ArgumentException("Offering type is required.", nameof(offeringType));

        var normalizedType = offeringType.Trim().ToLowerInvariant();
        var normalizedInstance = string.IsNullOrWhiteSpace(instance)
            ? null
            : instance.Trim().ToLowerInvariant();

        return new ToolFqid(normalizedType, normalizedInstance);
    }

    /// <summary>Whether this fqid has a specific instance qualifier.</summary>
    public bool IsQualified => Instance is not null;

    /// <summary>Whether this is the default (empty) value.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(OfferingType);

    /// <summary>
    /// Match this fqid (as a query/selector) against a candidate fqid.
    /// Bare name: "mongodb" matches "mongodb", "mongodb:prod", "mongodb:dev".
    /// Qualified: "mongodb:prod" matches only "mongodb:prod".
    /// </summary>
    public bool Matches(ToolFqid candidate)
    {
        if (IsEmpty) return true;

        if (IsQualified)
        {
            return string.Equals(OfferingType, candidate.OfferingType, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Instance, candidate.Instance, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(OfferingType, candidate.OfferingType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Match this fqid (as a query) against a tool snapshot, considering aliases and offering type.
    /// </summary>
    /// <param name="snapshotFqid">The tool's fqid string (e.g., "mongodb:prod").</param>
    /// <param name="snapshotOfferingType">The tool's offering type from tool.type (e.g., "mongodb").</param>
    /// <param name="aliases">Optional alias list from the tool.</param>
    public bool MatchesSnapshot(string snapshotFqid, string? snapshotOfferingType, IReadOnlyList<string>? aliases)
    {
        if (IsEmpty) return true;

        // Direct fqid match
        if (TryParse(snapshotFqid, out var candidate) && Matches(candidate))
            return true;

        // Bare name matches snapshot's offering type
        if (!IsQualified && snapshotOfferingType is not null &&
            string.Equals(OfferingType, snapshotOfferingType, StringComparison.OrdinalIgnoreCase))
            return true;

        // Alias match
        if (aliases is not null)
        {
            var canonical = ToString();
            for (var i = 0; i < aliases.Count; i++)
            {
                if (string.Equals(aliases[i], canonical, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string StripLegacyPrefix(string value)
    {
        if (value.StartsWith("offering:", StringComparison.OrdinalIgnoreCase))
            return value["offering:".Length..];

        if (value.StartsWith("seed-bank:", StringComparison.OrdinalIgnoreCase))
            return value["seed-bank:".Length..];

        return value;
    }

    /// <summary>Canonical string: "mongodb" or "mongodb:prod".</summary>
    public override string ToString()
    {
        return Instance is not null
            ? $"{OfferingType}{Separator}{Instance}"
            : OfferingType ?? string.Empty;
    }

    /// <summary>Implicit conversion from string (parses).</summary>
    public static implicit operator ToolFqid(string raw) => Parse(raw);

    /// <summary>Implicit conversion to string (canonical form).</summary>
    public static implicit operator string(ToolFqid fqid) => fqid.ToString();
}
