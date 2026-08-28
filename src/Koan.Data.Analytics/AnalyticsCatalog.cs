using Koan.Data.Abstractions.Analytics;

namespace Koan.Data.Analytics;

/// <summary>
/// The declared questions, keyed by name — the vocabulary every consumer shares. Declaration is active
/// (a question registers itself when the application's own code constructs it), so there is nothing to
/// discover at runtime and nothing for AOT to miss. The registry is process-global, the same lifetime
/// the static <c>Entity&lt;T&gt;</c> surface resolves through. Names are unique; a duplicate is a
/// startup failure, because two different answers to one name is a lie waiting for a meeting.
/// </summary>
public static class AnalyticsCatalog
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, AnalyticsQuestion> Questions = new(StringComparer.Ordinal);

    public static void Register(AnalyticsQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        lock (Gate)
        {
            if (Questions.ContainsKey(question.Name))
                throw new InvalidOperationException(
                    $"An analytics question named '{question.Name}' is already declared. " +
                    "Question names are the shared contract between code, endpoints, and agents — " +
                    "rename one of the declarations instead of overloading it.");
            Questions.Add(question.Name, question);
        }
    }

    public static bool TryGet(string name, out AnalyticsQuestion question)
    {
        lock (Gate) return Questions.TryGetValue(name, out question!);
    }

    /// <summary>Declared names in catalog order — deterministic, because agents read this.</summary>
    public static IReadOnlyList<string> Names()
    {
        lock (Gate) return Questions.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    public static IReadOnlyList<AnalyticsQuestion> All()
    {
        lock (Gate) return Questions.Values.OrderBy(static question => question.Name, StringComparer.Ordinal).ToArray();
    }

    public static int Count { get { lock (Gate) return Questions.Count; } }
}
