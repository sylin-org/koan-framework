using System.Collections.Generic;

namespace Koan.Canon.Options;

public sealed class CanonMaterializationOptions
{
    // Global default policy name when nothing else is specified
    public string DefaultPolicy { get; set; } = "last";

    // Per-model default policy name (overrides DefaultPolicy for a model)
    public Dictionary<string, string> PerModelDefaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Per-path override: key is "ModelName:Path"; value is policy name
    public Dictionary<string, string> PerPath { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Optional custom record transformers: modelName -> type full name
    public Dictionary<string, string> RecordTransformers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Optional custom property transformers: policyName -> type full name
    public Dictionary<string, string> PropertyTransformers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


