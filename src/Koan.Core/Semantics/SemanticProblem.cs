namespace Koan.Core.Semantics;

/// <summary>One deterministic semantic composition failure with an application-facing correction.</summary>
internal sealed record SemanticProblem(
    SemanticId Owner,
    string Reason,
    string Correction);
