using Koan.Data.Abstractions.Analytics;

namespace Koan.Data.Analytics;

/// <summary>
/// A golden question: a declared question paired with the assertion that its answer must satisfy. Kept
/// deployments of this feature class ran known-answer checks continuously; killed ones skipped them —
/// so the harness is part of the module, not an afterthought.
/// </summary>
public sealed class AnalyticsGoldenQuestion
{
    public required string QuestionName { get; init; }

    /// <summary>Returns null when the answer is right, otherwise the reason it is wrong.</summary>
    public required Func<AnalyticsResult, string?> Assert { get; init; }
}

public static class AnalyticsGoldenQuestions
{
    private static readonly object Gate = new();
    private static readonly List<AnalyticsGoldenQuestion> Golden = [];

    public static void Register(AnalyticsGoldenQuestion goldenQuestion)
    {
        ArgumentNullException.ThrowIfNull(goldenQuestion);
        lock (Gate) Golden.Add(goldenQuestion);
    }

    public static IReadOnlyList<AnalyticsGoldenQuestion> All()
    {
        lock (Gate) return Golden.ToArray();
    }
}

/// <summary>Runs every golden question and reports the reasons wrong answers gave — empty is the green state.</summary>
public static class AnalyticsHarness
{
    public static async Task<IReadOnlyList<string>> AuditAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var failures = new List<string>();
        foreach (var golden in AnalyticsGoldenQuestions.All())
        {
            if (!AnalyticsCatalog.TryGet(golden.QuestionName, out var question))
            {
                failures.Add($"[{golden.QuestionName}] no such question — the golden set is stale.");
                continue;
            }
            AnalyticsResult answer;
            try
            {
                answer = await question.ExecuteAsync(services, question.RowCap, ct).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add($"[{golden.QuestionName}] threw {error.GetType().Name}: {error.Message}");
                continue;
            }
            if (golden.Assert(answer) is { } reason)
                failures.Add($"[{golden.QuestionName}] {reason}");
        }
        return failures;
    }
}
