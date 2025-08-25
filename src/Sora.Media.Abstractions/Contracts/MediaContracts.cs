namespace Sora.Media.Abstractions;

public record MediaVariant(string? Type = null, int? Width = null, int? Height = null, int? Quality = null);

public record MediaTransformSpec(string Action, IReadOnlyDictionary<string, object?> Parameters);

public enum MediaTaskStatus { Pending, Processing, Completed, Failed, Cancelled }

public sealed record MediaTaskArg(string Name, string Type, bool Required, string? Default = null, IReadOnlyList<string>? Allowed = null, double? Min = null, double? Max = null, string? Pattern = null);

public sealed record MediaTaskStep(string Name, string Action, IReadOnlyDictionary<string, object?> SpecTemplate, string? Relationship = null, string? SavesTo = null, bool? ContinueOnError = null);

public sealed record MediaTaskDescriptor(string Code, int Version, string? Title, string? Summary, IReadOnlyList<MediaTaskArg> Args, IReadOnlyList<MediaTaskStep> Steps, IReadOnlyList<string>? Requires);

public sealed record MediaTaskRecord(string TaskId, string SourceMediaId, MediaTaskStatus Status, DateTimeOffset SubmittedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, IReadOnlyList<MediaTaskStepResult> Steps, string? ErrorCode = null, string? ErrorMessage = null);

public sealed record MediaTaskStepResult(string StepName, MediaTaskStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? OutputMediaId = null, string? ErrorCode = null, string? ErrorMessage = null, long? BytesRead = null, long? BytesWritten = null, double? CpuMs = null, double? MemoryMb = null);
