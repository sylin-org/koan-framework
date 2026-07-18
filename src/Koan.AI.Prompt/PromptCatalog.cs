namespace Koan.AI.Prompt;

/// <summary>
/// Resolves persisted prompt values from the Entity-backed prompt catalog.
/// </summary>
public static class PromptCatalog
{
    /// <summary>
    /// Loads the newest active version of a named prompt.
    /// </summary>
    public static async Task<Prompt> Load(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var entry = await PromptEntry.FindActive(name.Trim(), ct);
        return entry?.ToPrompt() ?? throw new PromptNotFoundException(name);
    }

    /// <summary>
    /// Loads one exact version of a named prompt, regardless of its lifecycle status.
    /// </summary>
    public static async Task<Prompt> Load(string name, int version, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        var entry = await PromptEntry.FindVersion(name.Trim(), version, ct);
        return entry?.ToPrompt() ?? throw new PromptNotFoundException(name, version);
    }
}
