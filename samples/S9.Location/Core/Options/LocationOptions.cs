using System.Collections.Generic;

namespace S9.Location.Core.Options;

public sealed class LocationOptions
{
    public const string SectionName = "S9:Location";

    public NormalizationOptions Normalization { get; set; } = new();
    public CacheOptions Cache { get; set; } = new();
    public AiAssistOptions AiAssist { get; set; } = new();

    public sealed class NormalizationOptions
    {
        public string DefaultCountry { get; set; } = "US";
        public string CaseMode { get; set; } = "Upper";
        public bool RemovePunctuation { get; set; } = true;
        public bool CompressWhitespace { get; set; } = true;
        public Dictionary<string, string> Abbreviations { get; set; } = new();
    }

    public sealed class CacheOptions
    {
        public bool Enabled { get; set; } = true;
        public double DefaultConfidence { get; set; } = 0.7;
    }

    public sealed class AiAssistOptions
    {
        public bool Enabled { get; set; } = false;
        public string Model { get; set; } = "mistral";
        public double ConfidenceThreshold { get; set; } = 0.85;
    }
}
