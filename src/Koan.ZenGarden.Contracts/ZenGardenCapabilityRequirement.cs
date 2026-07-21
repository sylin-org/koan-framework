namespace Koan.ZenGarden;

/// <summary>
/// A required capability used for subscription and catalog matching.
/// When Type is null, the requirement matches any capability type with the same Name.
/// </summary>
public sealed record ZenGardenCapabilityRequirement
{
    public required string Name { get; init; }
    public string? Type { get; init; }

    public string Canonical => Type is null ? Name : $"{Type}:{Name}";

    public static IReadOnlyList<ZenGardenCapabilityRequirement> ParseMany(IEnumerable<string> values)
    {
        var result = new List<ZenGardenCapabilityRequirement>();

        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var token in raw.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var trimmed = token.Trim().ToLowerInvariant();
                var index = trimmed.IndexOf(':');

                if (index > 0 && index < trimmed.Length - 1)
                {
                    result.Add(new ZenGardenCapabilityRequirement
                    {
                        Type = trimmed[..index],
                        Name = trimmed[(index + 1)..]
                    });
                }
                else
                {
                    result.Add(new ZenGardenCapabilityRequirement
                    {
                        Name = trimmed
                    });
                }
            }
        }

        return result
            .Distinct()
            .ToArray();
    }

    public bool Matches(IReadOnlyDictionary<string, IReadOnlyList<string>> capabilities)
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return false;
        }

        if (Type is not null)
        {
            if (!capabilities.TryGetValue(Type, out var typed))
            {
                return false;
            }

            return typed.Any(item => string.Equals(item, Name, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var pair in capabilities)
        {
            if (pair.Value.Any(item => string.Equals(item, Name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
