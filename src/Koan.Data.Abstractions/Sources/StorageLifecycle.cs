namespace Koan.Data.Abstractions.Sources;

/// <summary>Declares whether Koan may mutate the physical shape of a data source.</summary>
public enum StorageLifecycle
{
    Managed,
    External
}
