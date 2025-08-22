using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Core.Naming;

/// <summary>
/// Options for global fallback naming when no provider-specific defaults are registered.
/// Bind from configuration: Sora:Data:Naming:{Style,Separator,Casing}.
/// </summary>
public sealed class NamingFallbackOptions
{
    public StorageNamingStyle Style { get; set; } = StorageNamingStyle.EntityType;
    public string Separator { get; set; } = ".";
    public NameCasing Casing { get; set; } = NameCasing.AsIs;
}