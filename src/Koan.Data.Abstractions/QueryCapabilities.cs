namespace Koan.Data.Abstractions;

[Flags]
public enum QueryCapabilities
{
    None = 0,
    String = 1 << 0,
    Linq = 1 << 1,
}