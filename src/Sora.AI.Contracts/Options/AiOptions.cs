namespace Sora.AI.Contracts.Options;

public sealed class AiOptions
{
    public bool AutoDiscoveryEnabled { get; set; } = true;
    public bool AllowDiscoveryInNonDev { get; set; } = false;
    public string DefaultPolicy { get; set; } = "wrw+health+least-pending";
}
