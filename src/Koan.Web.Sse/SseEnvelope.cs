using System;

namespace Koan.Web.Sse;

/// <summary>
/// Represents a single Server-Sent Event frame.
/// </summary>
public readonly record struct SseEnvelope(
    string? EventName,
    string Data,
    string? Id = null,
    TimeSpan? Retry = null,
    string? Comment = null)
{
    public bool HasEventName => !string.IsNullOrWhiteSpace(EventName);
    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);
    internal bool IsControlFrame =>
        Data is { Length: 0 } &&
        (HasComment || Retry is not null || !string.IsNullOrEmpty(Id));
}
