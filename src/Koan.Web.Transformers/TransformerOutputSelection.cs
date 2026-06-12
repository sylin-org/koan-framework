using System.Collections.Generic;

namespace Koan.Web.Transformers;

/// <summary>
/// Combined output of <see cref="ITransformerRegistry"/> resolution for a given entity type +
/// request context. Carries the ordered pipeline of activated enrichers and the optional terminal
/// transformer selected by Accept negotiation.
/// </summary>
/// <param name="Pipeline">Enrichers in the order they should be applied. Empty when no enricher
/// activates.</param>
/// <param name="Terminal">Terminal (shape-changing) transformer if Accept negotiation selected one,
/// otherwise <c>null</c> — in which case the enriched value flows on to the default JSON path.</param>
public sealed record TransformerOutputSelection(
    IReadOnlyList<EnricherSelection> Pipeline,
    TransformerSelection? Terminal)
{
    public static TransformerOutputSelection Empty { get; } =
        new(System.Array.Empty<EnricherSelection>(), null);

    public bool HasAny => Pipeline.Count > 0 || Terminal is not null;
}
