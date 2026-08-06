namespace Koan.Data.Abstractions;

public sealed record StorageContainerDescriptor(
    StorageContainerReference Reference,
    StorageAddress Address,
    string DisplayPath,
    string ProviderKind,
    StorageContainerTraits Traits,
    StorageContainerOperations EffectiveOperations,
    IReadOnlyList<DataField>? RecordShape = null);
