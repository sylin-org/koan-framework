using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Core.Naming;

/// <summary>
/// Options for global fallback naming when no provider-specific defaults are registered.
/// Bind from configuration: Koan:Data:Naming:{Style,Separator,Casing}.
/// </summary>
public sealed class NamingFallbackOptions
{
    public StorageNamingStyle Style { get; set; } = StorageNamingStyle.EntityType;
    public string Separator { get; set; } = ".";
    public NameCasing Casing { get; set; } = NameCasing.AsIs;
}