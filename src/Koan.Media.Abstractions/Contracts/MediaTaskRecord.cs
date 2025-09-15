namespace Koan.Media.Abstractions.Contracts;

public sealed record MediaTaskRecord(string TaskId, string SourceMediaId, MediaTaskStatus Status, DateTimeOffset SubmittedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, IReadOnlyList<MediaTaskStepResult> Steps, string? ErrorCode = null, string? ErrorMessage = null);