namespace Koan.Data.Abstractions;

[Flags]
public enum StorageContainerOperations
{
    None = 0,
    Describe = 1,
    Sample = 2,
    Query = 4,
    Write = 8
}
