using Koan.Data.Core.Model;

namespace Koan.AI.Prompt;

/// <summary>
/// Entity-backed prompt storage. Enables versioning, A/B testing, and
/// editing by domain experts without code deploys.
///
/// <code>
/// var prompt = await Prompt.Load("support-response");          // Active version
/// var prompt = await Prompt.Load("support-response", version: 3); // Specific version
/// var prompt = await Prompt.Load("support-response", PromptStrategy.ABTest); // A/B test
/// </code>
/// </summary>
public class PromptEntry : Entity<PromptEntry>
{
    /// <summary>Logical name used for lookup (e.g., "support-response").</summary>
    public string Name { get; set; } = "";

    /// <summary>The prompt content — either raw text or serialized builder config.</summary>
    public string Content { get; set; } = "";

    /// <summary>System directive (separate from content for structured prompts).</summary>
    public string? SystemDirective { get; set; }

    /// <summary>Constraints (rules, guardrails).</summary>
    public List<string> Constraints { get; set; } = [];

    /// <summary>Auto-incremented version number within a name group.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Lifecycle status.</summary>
    public PromptStatus Status { get; set; } = PromptStatus.Draft;

    /// <summary>Who created or last edited this entry.</summary>
    public string? Author { get; set; }

    /// <summary>Change notes (e.g., "Made it more concise per Dana's feedback").</summary>
    public string? Notes { get; set; }

    /// <summary>Tags for organization.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Convert this entry to a Prompt object.</summary>
    public Prompt ToPrompt()
    {
        return Prompt.Create(p =>
        {
            if (SystemDirective is not null)
                p.System(SystemDirective);

            p.Instruct(Content);

            foreach (var constraint in Constraints)
                p.Constrain(constraint);

            p.Meta("name", Name);
            p.Meta("version", Version.ToString());
            if (Author is not null) p.Meta("author", Author);
        });
    }

    /// <summary>Find the active entry with the given name.</summary>
    internal static async Task<PromptEntry?> FindActive(
        string name, CancellationToken ct = default)
    {
        var entries = await PromptEntry.Query(
            e => e.Name == name && e.Status == PromptStatus.Active, ct);

        return entries
            .OrderByDescending(e => e.Version)
            .FirstOrDefault();
    }

    /// <summary>Find a specific version of a prompt.</summary>
    internal static async Task<PromptEntry?> FindVersion(
        string name, int version, CancellationToken ct = default)
    {
        var entries = await PromptEntry.Query(
            e => e.Name == name && e.Version == version, ct);

        return entries.FirstOrDefault();
    }

    /// <summary>Find all active versions (for A/B testing).</summary>
    internal static async Task<IReadOnlyList<PromptEntry>> FindAllActive(
        string name, CancellationToken ct = default)
    {
        var entries = await PromptEntry.Query(
            e => e.Name == name && e.Status == PromptStatus.Active, ct);

        return entries.OrderByDescending(e => e.Version).ToList();
    }
}

public enum PromptStatus
{
    /// <summary>Work in progress, not served.</summary>
    Draft,

    /// <summary>Live — served to users.</summary>
    Active,

    /// <summary>No longer served. Kept for history.</summary>
    Retired
}
