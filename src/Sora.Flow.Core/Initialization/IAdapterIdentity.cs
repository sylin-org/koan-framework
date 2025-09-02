using System;

namespace Sora.Flow.Initialization;

public interface IAdapterIdentity
{
    string? System { get; }
    string? Adapter { get; }
    string InstanceId { get; }
    string? Version { get; }
    string? Bus { get; }
    string? Group { get; }
    string[]? Capabilities { get; }
    string Host { get; }
    int Pid { get; }
    DateTimeOffset StartedAt { get; }
    int HeartbeatSeconds { get; }
}
