namespace Sora.Flow.Options;

public sealed class AdapterRegistryOptions
{
    public int TtlSeconds { get; set; } = 120;
    public int HeartbeatSeconds { get; set; } = 30;
    public bool AutoAnnounce { get; set; } = true;
    public bool ReplyOnCommand { get; set; } = true;
}
