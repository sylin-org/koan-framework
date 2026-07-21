namespace Koan.Storage.Abstractions;

/// <summary>The provider's physical role in Storage topology.</summary>
public enum StorageProviderPlacement
{
    Local = 0,
    Remote = 1,
    Composite = 2
}
