namespace Koan.Web.Sse.Options;

/// <summary>
/// The stream defaults: the event name typed or text values take when they declare none (explicit
/// <see cref="SseEnvelope"/> streams preserve unnamed protocol frames), how long a stream may stay silent, and what
/// <see cref="SseHub{T}"/> holds.
/// </summary>
public sealed class KoanSseOptions
{
    public const string DefaultEventName = "message";

    public static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(15);

    private string _defaultEvent = DefaultEventName;

    public string DefaultEvent
    {
        get => string.IsNullOrWhiteSpace(_defaultEvent) ? DefaultEventName : _defaultEvent;
        set => _defaultEvent = string.IsNullOrWhiteSpace(value) ? DefaultEventName : value.Trim();
    }

    /// <summary>
    /// A stream that has written nothing for this long writes a comment, so a proxy keeps it open and a reader knows it
    /// lives. Zero or less sends none. Configure via: Koan:Web:Sse:Heartbeat.
    /// </summary>
    public TimeSpan Heartbeat { get; set; } = DefaultHeartbeat;

    /// <summary>What every <see cref="SseHub{T}"/> in the host holds.</summary>
    public SseHubOptions Hub { get; set; } = new();
}

/// <summary>What one <see cref="SseHub{T}"/> holds: each open stream costs a queue and a request, so these size memory.</summary>
public sealed class SseHubOptions
{
    /// <summary>The most values a stream's queue holds before the stream is closed as too slow, to resume from where it was.</summary>
    public int QueueBound { get; set; } = 256;

    /// <summary>The most streams one reader holds open at once. A browser opens six to one host over HTTP/1.1.</summary>
    public int PerReader { get; set; } = 6;

    /// <summary>The most streams open at once in all.</summary>
    public int InAll { get; set; } = 512;
}
