namespace Koan.Data.Abstractions.Filtering;

/// <summary>
/// Result of translating a filter for one adapter: the provider-native query for the part
/// that was pushed down, plus the <see cref="Residual"/> filter (if any) that the caller must
/// evaluate in memory. <see cref="Residual"/> is null when the whole filter was pushed.
/// </summary>
public sealed record FilterTranslation<TNative>(TNative? Pushed, Filter? Residual)
{
    public bool HasPushed => Pushed is not null;
    public bool FullyPushed => Residual is null;
}

/// <summary>
/// The single per-adapter translation contract (anti-corruption layer between the normalized
/// <see cref="Filter"/> model and a provider query language). An adapter declares its
/// <see cref="Capabilities"/> and translates the pushable portion to <typeparamref name="TNative"/>,
/// returning the unpushable remainder as a residual <see cref="Filter"/>. Replaces the
/// relational <c>LinqWhereTranslator</c>, the Couchbase translator, the per-vector translators,
/// and the Mongo GUID walker with one shape.
/// </summary>
public interface IFilterTranslator<TNative>
{
    FilterCapabilities Capabilities { get; }

    FilterTranslation<TNative> Translate(Filter filter, Type entityType);
}
