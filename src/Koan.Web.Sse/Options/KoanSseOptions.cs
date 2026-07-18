namespace Koan.Web.Sse.Options;

/// <summary>
/// Controls the default event name used when an SSE envelope does not declare one.
/// </summary>
public sealed class KoanSseOptions
{
    public const string DefaultEventName = "message";

    private string _defaultEvent = DefaultEventName;

    public string DefaultEvent
    {
        get => string.IsNullOrWhiteSpace(_defaultEvent) ? DefaultEventName : _defaultEvent;
        set => _defaultEvent = string.IsNullOrWhiteSpace(value) ? DefaultEventName : value.Trim();
    }
}
