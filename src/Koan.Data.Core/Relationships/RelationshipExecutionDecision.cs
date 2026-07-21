namespace Koan.Data.Core.Relationships;

/// <summary>Safe, inspectable outcome of relationship backend negotiation.</summary>
public sealed record RelationshipExecutionDecision(
    RelationshipExecutionMode Mode,
    string Provider,
    int ParentCount,
    int ResultCount,
    int? CandidatesExamined,
    int? CandidateLimit);
