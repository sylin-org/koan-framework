using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Subscription predicate used by runtime event watchers.
/// </summary>
public sealed record ZenGardenSubscription
{
    public ZenGardenToolType? ToolType { get; init; }
    public string? ToolFqid { get; init; }
    public IReadOnlyList<ZenGardenCapabilityRequirement> Requires { get; init; } = Array.Empty<ZenGardenCapabilityRequirement>();

    public static ZenGardenSubscription ForOffering(string offeringOrSelector)
    {
        var parsed = ParseOfferingSelector(offeringOrSelector);
        return new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.Offering,
            ToolFqid = parsed.ToolFqid,
            Requires = parsed.Requires
        };
    }

    public static ZenGardenSubscription ForStorage(string seedBank)
    {
        var fqid = NormalizeSeedBank(seedBank);
        return new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank,
            ToolFqid = fqid
        };
    }

    public ZenGardenSubscription Require(params string[] capabilities)
    {
        var merged = Requires
            .Concat(ZenGardenCapabilityRequirement.ParseMany(capabilities))
            .Distinct()
            .ToArray();

        return this with { Requires = merged };
    }

    public bool Matches(ZenGardenToolSnapshot snapshot)
    {
        if (ToolType is not null && snapshot.ToolType != ToolType.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ToolFqid) &&
            !string.Equals(snapshot.ToolFqid, ToolFqid, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public bool RequirementsSatisfiedBy(ZenGardenToolSnapshot snapshot)
    {
        if (Requires.Count == 0)
        {
            return true;
        }

        foreach (var requirement in Requires)
        {
            if (!requirement.Matches(snapshot.Capabilities))
            {
                return false;
            }
        }

        return true;
    }

    public static ZenGardenSubscription Parse(string selector)
    {
        return ForOffering(selector);
    }

    private static (string ToolFqid, IReadOnlyList<ZenGardenCapabilityRequirement> Requires) ParseOfferingSelector(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new ArgumentException("Offering selector is required.", nameof(selector));
        }

        var trimmed = selector.Trim();
        var requires = Array.Empty<ZenGardenCapabilityRequirement>();

        var bracketStart = trimmed.IndexOf('[');
        if (bracketStart >= 0)
        {
            var bracketEnd = trimmed.LastIndexOf(']');
            if (bracketEnd <= bracketStart)
            {
                throw new ArgumentException($"Invalid offering selector '{selector}'. Missing closing ']'.", nameof(selector));
            }

            var capabilities = trimmed[(bracketStart + 1)..bracketEnd];
            requires = ZenGardenCapabilityRequirement.ParseMany([capabilities]).ToArray();
            trimmed = trimmed[..bracketStart].Trim();
        }

        var fqid = NormalizeOffering(trimmed);
        return (fqid, requires);
    }

    private static string NormalizeOffering(string offering)
    {
        var normalized = offering.Trim().ToLowerInvariant();
        if (normalized.StartsWith("offering:", StringComparison.Ordinal))
        {
            return normalized;
        }

        return $"offering:{normalized}";
    }

    private static string NormalizeSeedBank(string seedBank)
    {
        var normalized = seedBank.Trim().ToLowerInvariant();
        if (normalized.StartsWith("seed-bank:", StringComparison.Ordinal))
        {
            return normalized;
        }

        return $"seed-bank:{normalized}";
    }
}
