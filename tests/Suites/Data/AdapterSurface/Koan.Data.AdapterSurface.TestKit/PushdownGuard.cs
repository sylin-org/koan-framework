using AwesomeAssertions;
using Koan.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.AdapterSurface.TestKit;

/// <summary>
/// Fails a spec when the framework finished part of a query in memory that the store was asked to do.
///
/// <para>Comparing answers cannot see this. A filter evaluated in memory, a sort finished by the framework, a
/// page taken after materializing — all of them return exactly the right rows, so an oracle that checks rows
/// stays green while the query quietly reads the whole table. Every pushdown gap found in this repository was
/// found by reading adapter source, never by a passing suite noticing.</para>
///
/// <para>Koan records the fallback where it decides it, so the runtime explanation doubles as the assertion:
/// this reads the same fact an operator would read at <c>/.well-known/Koan/facts</c>.</para>
/// </summary>
public static class PushdownGuard
{
    private const string FallbackCode = "koan.data.query.fallback";

    /// <summary>Runs <paramref name="work"/> and fails if it left a new in-memory-fallback fact behind.</summary>
    public static async Task NothingFallsBack(IServiceProvider services, string what, Func<Task> work)
    {
        var before = Fallbacks(services);
        await work();
        var after = Fallbacks(services);

        after.Where(fact => !before.Contains(fact)).Should().BeEmpty(
            $"{what} must be executed by the store, not finished in memory");
    }

    /// <summary>The fallback facts the host currently holds, as "subject: summary" for a legible failure.</summary>
    public static IReadOnlyCollection<string> Fallbacks(IServiceProvider services)
    {
        var facts = services.GetService<IKoanRuntimeFacts>();
        if (facts is null) return [];
        return facts.Current.Facts
            .Where(fact => string.Equals(fact.Code, FallbackCode, StringComparison.Ordinal))
            .Select(fact => $"{fact.Subject}: {fact.Summary}")
            .ToArray();
    }
}
