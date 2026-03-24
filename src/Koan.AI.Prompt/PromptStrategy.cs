namespace Koan.AI.Prompt;

/// <summary>
/// Strategy for resolving which prompt version to use.
/// Supports A/B testing, canary rollout, and pinned versions.
/// </summary>
public abstract record PromptStrategy
{
    /// <summary>Randomly select between active versions (50/50 split).</summary>
    public static PromptStrategy ABTest => new ABTestStrategy();

    /// <summary>Serve the latest version to a percentage of requests.</summary>
    public static PromptStrategy Canary(double percentage) => new CanaryStrategy(percentage);

    /// <summary>Always use the latest active version.</summary>
    public static PromptStrategy Latest => new LatestStrategy();

    /// <summary>Always use a specific version.</summary>
    public static PromptStrategy Pinned(int version) => new PinnedStrategy(version);

    /// <summary>Resolve a PromptEntry using this strategy.</summary>
    internal abstract Task<PromptEntry?> Resolve(
        string name, CancellationToken ct = default);
}

internal sealed record ABTestStrategy : PromptStrategy
{
    private static readonly Random Rng = Random.Shared;

    internal override async Task<PromptEntry?> Resolve(
        string name, CancellationToken ct)
    {
        var entries = await PromptEntry.FindAllActive(name, ct);
        if (entries.Count == 0) return null;
        return entries[Rng.Next(entries.Count)];
    }
}

internal sealed record CanaryStrategy(double Percentage) : PromptStrategy
{
    private static readonly Random Rng = Random.Shared;

    internal override async Task<PromptEntry?> Resolve(
        string name, CancellationToken ct)
    {
        var entries = await PromptEntry.FindAllActive(name, ct);
        if (entries.Count == 0) return null;
        if (entries.Count == 1) return entries[0];

        // entries[0] = latest (highest version), entries[^1] = stable (lowest active version)
        var useLatest = Rng.NextDouble() < Percentage;
        return useLatest ? entries[0] : entries[^1];
    }
}

internal sealed record LatestStrategy : PromptStrategy
{
    internal override async Task<PromptEntry?> Resolve(
        string name, CancellationToken ct)
    {
        return await PromptEntry.FindActive(name, ct);
    }
}

internal sealed record PinnedStrategy(int Version) : PromptStrategy
{
    internal override async Task<PromptEntry?> Resolve(
        string name, CancellationToken ct)
    {
        return await PromptEntry.FindVersion(name, Version, ct);
    }
}
