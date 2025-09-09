namespace S8.Location.Core.Options;

public sealed class LocationOptions
{
    public OrchestratorOptions Orchestrator { get; set; } = new();
    public ResolutionOptions Resolution { get; set; } = new();
    public GeocodingOptions Geocoding { get; set; } = new();
    public AiOptions Ai { get; set; } = new();
}

public class OrchestratorOptions
{
    public string ProcessingMode { get; set; } = "Sequential";
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
}

public class ResolutionOptions
{
    public bool CacheEnabled { get; set; } = true;
    public int CacheTTLHours { get; set; } = 720; // 30 days
    public NormalizationRules NormalizationRules { get; set; } = new();
}

public class NormalizationRules
{
    public string CaseMode { get; set; } = "Upper";
    public bool RemovePunctuation { get; set; } = true;
    public bool CompressWhitespace { get; set; } = true;
}

public class GeocodingOptions
{
    public string Primary { get; set; } = "GoogleMaps";
    public string Fallback { get; set; } = "OpenStreetMap";
    public decimal MaxMonthlyBudget { get; set; } = 250.00m;
    public string? GoogleMapsApiKey { get; set; }
}

public class AiOptions
{
    public string Model { get; set; } = "llama3.1:8b";
    public int TimeoutMs { get; set; } = 5000;
}