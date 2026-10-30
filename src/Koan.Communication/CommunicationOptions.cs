namespace Koan.Communication;

/// <summary>Host-owned Communication provider bindings and publication limits.</summary>
public sealed class CommunicationOptions
{
    /// <summary>
    /// Startup-declared business channels. Each channel uses normal provider election unless a lane is pinned.
    /// </summary>
    public Dictionary<string, CommunicationChannelOptions> Channels { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Optional deployment pin for the default Transport channel.</summary>
    public string? TransportProvider { get; set; }

    /// <summary>Optional deployment pin for the default Events channel.</summary>
    public string? EventsProvider { get; set; }

    /// <summary>Optional deployment pin for framework-owned internal signals.</summary>
    public string? FrameworkSignalsProvider { get; set; }

    /// <summary>Optional deployment pin for framework-owned every-node broadcasts.</summary>
    public string? FrameworkBroadcastsProvider { get; set; }

    /// <summary>
    /// Memory, in bytes, that the publications waiting for one local receiver, subscription, or framework group may
    /// hold. It counts each waiting publication's encoded size; a publication shared by several groups counts once in
    /// each. A group's memory exceeds it by at most one publication, plus per-delivery bookkeeping and the one
    /// delivery in flight. A publisher never waits on it, and a handler whose backlog stays within it never loses a
    /// delivery. A group whose waiting publications reach it fails further deliveries visibly, in settlement and the
    /// log, until its handler catches up; the publisher and every other group are unaffected.
    /// </summary>
    public long InProcessBacklogBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// How long one handler invocation may run. When it passes, the handler's cancellation token is cancelled, and a
    /// handler that ends because of it is a failed delivery. A handler that ignores its token keeps only its own
    /// group waiting.
    /// </summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromMinutes(2);
}
