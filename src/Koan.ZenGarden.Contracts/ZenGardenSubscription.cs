using Koan.ZenGarden.Models;

namespace Koan.ZenGarden;

/// <summary>
/// Subscription predicate used by runtime event watchers.
/// </summary>
public sealed record ZenGardenSubscription
{
    public ZenGardenToolType? ToolType { get; init; }
    public string? ToolFqid { get; init; }
    public IReadOnlyList<ZenGardenCapabilityRequirement> Requires { get; init; } = [];

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
        return new ZenGardenSubscription
        {
            ToolType = ZenGardenToolType.SeedBank,
            ToolFqid = Koan.ZenGarden.ToolFqid.Parse(seedBank).ToString()
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

        if (!string.IsNullOrWhiteSpace(ToolFqid))
        {
            var query = Koan.ZenGarden.ToolFqid.Parse(ToolFqid);
            if (!query.MatchesSnapshot(snapshot.ToolFqid, snapshot.OfferingType, null))
            {
                return false;
            }
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
        ZenGardenCapabilityRequirement[] requires = [];

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

        var fqid = Koan.ZenGarden.ToolFqid.Parse(trimmed).ToString();
        return (fqid, requires);
    }
}
