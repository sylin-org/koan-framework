using Koan.AI.Contracts.Categories;

namespace Koan.AI.Contracts.Options;

public sealed class AiOptions
{
    public bool AutoDiscoveryEnabled { get; set; } = true;
    public bool AllowDiscoveryInNonDev { get; set; } = false;
    public string DefaultPolicy { get; set; } = "wrw+health+least-pending";

    /// <summary>
    /// Interval in seconds between background health probes for AI source members.
    /// Set to 0 to disable health monitoring. Default: 30.
    /// Bound to <c>Koan:Ai:HealthProbeIntervalSeconds</c>.
    /// </summary>
    public int HealthProbeIntervalSeconds { get; set; } = 30;

    /// <summary>Chat category configuration. Bound to <c>Koan:Ai:Chat</c>.</summary>
    public AiCategoryOptions Chat { get; set; } = new();

    /// <summary>Embed category configuration. Bound to <c>Koan:Ai:Embed</c>.</summary>
    public AiCategoryOptions Embed { get; set; } = new();

    /// <summary>Ocr category configuration. Bound to <c>Koan:Ai:Ocr</c>. Defaults Via = "Chat".</summary>
    public AiCategoryOptions Ocr { get; set; } = new() { Via = "Chat", Model = "glm-ocr" };
}
