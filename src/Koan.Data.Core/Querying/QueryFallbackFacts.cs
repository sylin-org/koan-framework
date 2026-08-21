using Koan.Core.Diagnostics;
using Koan.Core.Hosting.App;

namespace Koan.Data.Core.Querying;

/// <summary>
/// Says out loud when a query was finished in the framework rather than by the store.
///
/// <para>Falling back is not always wrong — a store that cannot express an operator has to be carried, and the
/// JSON floor has no query engine at all. What is wrong is doing it silently. A filter, sort, page or
/// projection that quietly moves into memory turns a bounded query into a full read, and the application sees
/// nothing but a slower response and, eventually, a larger machine. Every gap of that kind found in this
/// repository was found by reading an adapter's source, never by running it.</para>
///
/// <para>So the decision is recorded where it is made, on the same explanation channel as provider election:
/// <c>/.well-known/Koan/facts</c> shows which Entity fell back, on which axes, under which provider. Facts are
/// a snapshot keyed by code and subject, so a hot loop replaces its own entry rather than accumulating.</para>
///
/// <para>Nothing is resolved unless a fallback actually happened, so a query that pushes down entirely pays
/// for one boolean.</para>
/// </summary>
internal static class QueryFallbackFacts
{
    public static void Record<TEntity>(bool filter, bool sort, bool pagination, bool projection)
    {
        var services = AppHost.Current;
        if (services?.GetService(typeof(IKoanRuntimeFactRecorder)) is not IKoanRuntimeFactRecorder facts) return;

        var axes = new List<string>(4);
        if (filter) axes.Add("filter");
        if (sort) axes.Add("sort");
        if (pagination) axes.Add("pagination");
        if (projection) axes.Add("projection");
        if (axes.Count == 0) return;

        var entity = typeof(TEntity).FullName ?? typeof(TEntity).Name;
        var joined = string.Join(", ", axes);
        facts.Record(new KoanFactDescriptor(
            Infrastructure.Constants.Diagnostics.Codes.QueryFallback,
            KoanFactKind.Capability,
            KoanFactState.Selected,
            $"query:{entity}",
            $"Koan finished {joined} in memory for {typeof(TEntity).Name}; the provider did not.",
            Infrastructure.Constants.Diagnostics.Reasons.QueryFinishedInMemory,
            "The whole candidate set was materialized to do it. Check that the adapter declares the capability " +
            "it has, and that the query uses a shape it can push down.",
            "Koan.Data.Core",
            entity));
    }
}
