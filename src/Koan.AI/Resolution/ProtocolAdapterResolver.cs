using Koan.AI.Contracts.Adapters;
using Koan.AI.Contracts.Routing;
using Koan.AI.Contracts.Sources;

namespace Koan.AI.Resolution;

/// <summary>
/// Resolves the adapter behind a protocol verb (Imagine, Transcribe, Speak, Rerank, Edit, Render) by the same
/// rules as the category router: an explicit or scoped source is required and never weakened to another
/// provider; without one the elected provider serves, and two providers with no election are an error.
/// </summary>
internal static class ProtocolAdapterResolver
{
    private const string MemberSeparator = "::";

    /// <param name="source">The explicit source, else the scoped one, as merged by the caller; null when none is in play.</param>
    public static TAdapter Resolve<TAdapter>(
        IAiAdapterRegistry adapters,
        IAiSourceRegistry sources,
        string capability,
        string? source)
        where TAdapter : class, IAiAdapter
        => string.IsNullOrWhiteSpace(source)
            ? Elect<TAdapter>(adapters, sources, capability)
            : FromSource<TAdapter>(adapters, sources, capability, source);

    private static TAdapter FromSource<TAdapter>(
        IAiAdapterRegistry adapters,
        IAiSourceRegistry sources,
        string capability,
        string sourceHint)
        where TAdapter : class, IAiAdapter
    {
        var separator = sourceHint.IndexOf(MemberSeparator, StringComparison.Ordinal);
        var name = (separator < 0 ? sourceHint : sourceHint[..separator]).Trim();

        var source = sources.GetSource(name);
        if (source is null)
        {
            var memberContext = separator < 0 ? string.Empty : $" for member reference '{sourceHint}'";
            throw new InvalidOperationException(
                $"Source '{name}' not found{memberContext}. " +
                $"Usable sources for capability '{capability}': {Choices(UsableSources<TAdapter>(adapters, sources))}");
        }

        if (!source.IsEnabled)
        {
            throw new InvalidOperationException(
                $"AI source '{source.Name}' is disabled. Enable it through IAiSourceControl or select another source. " +
                $"Usable sources for capability '{capability}': {Choices(UsableSources<TAdapter>(adapters, sources))}");
        }

        var adapter = adapters.Get(source.Provider)
            ?? throw new InvalidOperationException(
                $"No adapter found for provider '{source.Provider}' of source '{source.Name}'. " +
                $"Available adapters: {Choices(adapters.All.Select(candidate => candidate.Id))}");

        return adapter as TAdapter
            ?? throw new InvalidOperationException(
                $"Source '{source.Name}' (provider '{source.Provider}') does not support capability '{capability}'. " +
                $"Usable sources: {Choices(UsableSources<TAdapter>(adapters, sources))}");
    }

    private static TAdapter Elect<TAdapter>(
        IAiAdapterRegistry adapters,
        IAiSourceRegistry sources,
        string capability)
        where TAdapter : class, IAiAdapter
    {
        var all = sources.GetAllSources();
        var implementing = adapters.All.OfType<TAdapter>().ToArray();

        // A provider is a candidate unless every source that names it is disabled.
        var candidates = implementing
            .Select(adapter => (Adapter: adapter, Sources: SourcesOf(all, adapter)))
            .Where(candidate => candidate.Sources.Length == 0 || candidate.Sources.Any(source => source.IsEnabled))
            .ToArray();

        if (candidates.Length == 0)
        {
            var disabled = implementing.Length == 0
                ? string.Empty
                : $" Every source of {Choices(implementing.Select(adapter => adapter.Id))} is disabled; enable one through IAiSourceControl.";
            throw new InvalidOperationException(
                $"No adapter registered for {typeof(TAdapter).Name}. " +
                $"Add a provider that supports the '{capability}' capability.{disabled}");
        }

        if (candidates.Length == 1) return candidates[0].Adapter;

        // The election: the strictly highest enabled-source priority wins; a provider with no source ranks last.
        var ranked = candidates
            .Select(candidate => (
                candidate.Adapter,
                candidate.Sources,
                Priority: candidate.Sources.Where(source => source.IsEnabled)
                    .Select(source => source.Priority)
                    .DefaultIfEmpty(int.MinValue)
                    .Max()))
            .OrderByDescending(candidate => candidate.Priority)
            .ToArray();

        if (ranked[0].Priority != ranked[1].Priority) return ranked[0].Adapter;

        var tied = ranked.Where(candidate => candidate.Priority == ranked[0].Priority).ToArray();
        var sourceNames = tied.SelectMany(candidate => candidate.Sources.Where(source => source.IsEnabled))
            .Select(source => source.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        throw new InvalidOperationException(
            $"Multiple providers implement {typeof(TAdapter).Name} for capability '{capability}' and none is elected: " +
            $"{Choices(tied.Select(candidate => candidate.Adapter.Id))}. " +
            $"Select one with Source (sources: {Choices(sourceNames)}) or Client.Scope, " +
            "or give one provider's source a higher Priority.");
    }

    private static AiSourceDefinition[] SourcesOf(IEnumerable<AiSourceDefinition> sources, IAiAdapter adapter)
        => sources.Where(source => string.Equals(source.Provider?.Trim(), adapter.Id?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static IEnumerable<string> UsableSources<TAdapter>(IAiAdapterRegistry adapters, IAiSourceRegistry sources)
        where TAdapter : class, IAiAdapter
        => sources.GetAllSources()
            .Where(source => source.IsEnabled && adapters.Get(source.Provider) is TAdapter)
            .OrderByDescending(source => source.Priority)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(source => source.Name);

    private static string Choices(IEnumerable<string> choices)
    {
        var values = choices.ToArray();
        return values.Length == 0 ? "(none)" : string.Join(", ", values);
    }
}
