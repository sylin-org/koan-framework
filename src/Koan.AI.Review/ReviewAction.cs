namespace Koan.AI.Review;

/// <summary>
/// Base class for review actions that can be performed on a reviewable entity.
/// Each subclass defines a specific kind of human feedback.
/// </summary>
public abstract record ReviewAction<T>;

/// <summary>Approve the entity as-is.</summary>
public sealed record ApproveAction<T>() : ReviewAction<T>;

/// <summary>Reject the entity, optionally requiring a reason.</summary>
public sealed record RejectAction<T>(bool RequireReason = false) : ReviewAction<T>;

/// <summary>Edit a specific field on the entity.</summary>
public sealed record EditAction<T>(string FieldName) : ReviewAction<T>;

/// <summary>Label a specific field on the entity with one of the provided options.</summary>
public sealed record LabelAction<T>(string FieldName, object[] Options) : ReviewAction<T>;

/// <summary>Flag the entity with one of the provided flag types.</summary>
public sealed record FlagAction<T>(string[] FlagTypes) : ReviewAction<T>;
