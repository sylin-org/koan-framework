using System;

namespace S12.MedTrials.Infrastructure.Mcp;

public sealed class McpBridgeOptions
{
    public const string SectionName = "S12:MedTrials:Mcp";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:5114/mcp/";

    public int ProbeIntervalSeconds { get; set; } = 60;

    public bool LogCapabilities { get; set; } = true;

    internal Uri? TryGetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return null;
        }

        var normalized = BaseUrl.EndsWith("/", StringComparison.Ordinal)
            ? BaseUrl
            : BaseUrl + "/";

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ? uri : null;
    }

    internal TimeSpan GetProbeInterval() => TimeSpan.FromSeconds(Math.Clamp(ProbeIntervalSeconds <= 0 ? 60 : ProbeIntervalSeconds, 5, 3600));
}
