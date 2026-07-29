namespace Koan.Data.Abstractions;

/// <summary>
/// A root-object codec that proves which logical paths it excludes so independent bindings retain authority.
/// </summary>
public interface IRootObjectMappingCodec : IDataMappingCodec
{
    IReadOnlySet<MappingPath> ExcludedLogicalPaths { get; }
}
