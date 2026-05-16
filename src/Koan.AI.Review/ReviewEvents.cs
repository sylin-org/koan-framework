namespace Koan.AI.Review;

/// <summary>Raised when a reviewer approves an entity.</summary>
public sealed record ReviewApproved(
    string EntityType,
    string EntityId,
    string Queue,
    string ReviewedBy);

/// <summary>Raised when a reviewer rejects an entity.</summary>
public sealed record ReviewRejected(
    string EntityType,
    string EntityId,
    string Queue,
    string ReviewedBy,
    string Reason);

/// <summary>Raised when a reviewer edits a field on an entity.</summary>
public sealed record ReviewEdited(
    string EntityType,
    string EntityId,
    string Queue,
    string ReviewedBy,
    string Field);

/// <summary>Raised when a reviewer flags an entity.</summary>
public sealed record ReviewFlagged(
    string EntityType,
    string EntityId,
    string Queue,
    string ReviewedBy,
    string FlagType);
