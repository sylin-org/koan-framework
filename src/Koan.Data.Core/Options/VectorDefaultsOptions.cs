namespace Koan.Data.Core.Options;

public sealed class VectorDefaultsOptions
{
    public int DefaultTopK { get; set; } = 10;
    public int MaxTopK { get; set; } = 200;
    public int DefaultTimeoutSeconds { get; set; } = 10;
    // Optional default provider used when no [VectorAdapter] is specified and multiple vector factories exist.
    public string? DefaultProvider { get; set; }
}
