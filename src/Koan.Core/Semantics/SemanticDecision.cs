namespace Koan.Core.Semantics;

internal enum SemanticDecisionState
{
    Active,
    Inactive,
    Rejected,
}

/// <summary>One immutable activation decision projected by every diagnostic surface.</summary>
internal sealed record SemanticDecision(
    SemanticId Component,
    SemanticDecisionState State,
    string Reason,
    SemanticEvidence? Evidence);
