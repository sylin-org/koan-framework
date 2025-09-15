namespace Koan.Data.Core;

public sealed class DataRuntimeOptions
{
    // If true, the runtime will attempt to ensure schemas exist for known entities on start.
    public bool EnsureSchemaOnStart { get; set; } = true;
}
