namespace Koan.Web.Options;

/// <summary>
/// Toggles for optional pipeline components managed by Koan.
/// </summary>
public sealed class WebPipelineOptions
{
    public bool UseExceptionHandler { get; set; }
    public bool UseRateLimiter { get; set; }
}
