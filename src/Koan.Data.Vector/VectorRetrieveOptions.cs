using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector;

/// <summary>
/// The typed retrieval-options seam shared by every AI read path (Chain.Retrieve, RAG, agent
/// {type}_search) — AI-0036 §10 R4. It maps 1:1 onto <c>Vector&lt;T&gt;.Search</c>'s real surface so a
/// knob can never again be silently dropped by hand-marshalled positional reflection (the R1/R2/R4
/// bug). It lives in <c>Koan.Data.Vector</c> so all three orchestration pillars reference one type
/// with no AI-&gt;store-internals dependency edge.
/// </summary>
/// <remarks>
/// Every field defaults to the no-op so an existing call site that builds a bare options behaves
/// exactly as today: no <see cref="Text"/>/<see cref="Alpha"/> = pure-vector kNN, no
/// <see cref="Filter"/> = match-all, no <see cref="Rerank"/>.
/// </remarks>
public sealed record VectorRetrieveOptions
{
    /// <summary>Hybrid lexical text. When set (with <see cref="Alpha"/>), enables hybrid search.</summary>
    public string? Text { get; init; }

    /// <summary>Hybrid semantic-vs-keyword weight. <c>null</c> = pure-vector.</summary>
    public double? Alpha { get; init; }

    /// <summary>Maximum results.</summary>
    public int? TopK { get; init; }

    /// <summary>Metadata filter (the unified <see cref="Filter"/> AST). <c>null</c> = match-all.</summary>
    public Filter? Filter { get; init; }

    /// <summary>When true, re-score retrieved passages by relevance.</summary>
    public bool Rerank { get; init; }
}
