using System;

namespace Koan.Web.Sse.Options;

public sealed class KoanSseOptions
{
    public const string DefaultEventName = "message";
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

    public bool Enabled { get; set; } = true;

    private string _defaultEvent = DefaultEventName;

    public string DefaultEvent
    {
        get => string.IsNullOrWhiteSpace(_defaultEvent) ? DefaultEventName : _defaultEvent;
        set => _defaultEvent = string.IsNullOrWhiteSpace(value) ? DefaultEventName : value.Trim();
    }

    public TimeSpan HeartbeatInterval { get; set; } = DefaultHeartbeatInterval;
}
