namespace Sora.Web;

/// <summary>
/// Toggles for optional pipeline components managed by Sora.
/// </summary>
public sealed class WebPipelineOptions
{
    public bool UseExceptionHandler { get; set; }
    public bool UseRateLimiter { get; set; }
}
