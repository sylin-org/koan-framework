namespace Koan.Data.Abstractions;

[Flags]
public enum StorageContainerTraits
{
    None = 0,
    Records = 1,
    Physical = 2,
    Virtual = 4,
    ReadOnly = 8
}
