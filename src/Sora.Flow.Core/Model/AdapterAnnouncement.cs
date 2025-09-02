using System;

namespace Sora.Flow.Model;

public sealed class AdapterAnnouncement
{
    public required string System { get; init; }
    public required string Adapter { get; init; }
    public required string InstanceId { get; init; }
    public string? Version { get; init; }
    public string[]? Capabilities { get; init; }
    public string? Bus { get; init; }
    public string? Group { get; init; }
    public string? Host { get; init; }
    public int? Pid { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public int HeartbeatSeconds { get; init; }
}
