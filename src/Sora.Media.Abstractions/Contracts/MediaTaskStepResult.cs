namespace Sora.Media.Abstractions.Contracts;

public sealed record MediaTaskStepResult(string StepName, MediaTaskStatus Status, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, string? OutputMediaId = null, string? ErrorCode = null, string? ErrorMessage = null, long? BytesRead = null, long? BytesWritten = null, double? CpuMs = null, double? MemoryMb = null);