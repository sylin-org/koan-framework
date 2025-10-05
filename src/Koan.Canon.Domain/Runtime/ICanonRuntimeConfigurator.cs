namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Allows modules to contribute canon runtime configuration without owning the runtime builder lifecycle.
/// </summary>
public interface ICanonRuntimeConfigurator
{
    /// <summary>
    /// Applies pipeline configuration to the provided builder.
    /// </summary>
    void Configure(CanonRuntimeBuilder builder);
}
