using System.Collections.Immutable;

namespace Koan.Core.Semantics;

/// <summary>Stable, safe evidence explaining why one component was or was not activated.</summary>
internal sealed record SemanticEvidence(
    string Kind,
    string Source,
    ImmutableArray<SemanticId> Path);
