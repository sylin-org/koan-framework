namespace Sora.Data.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class DataAdapterAttribute : Attribute
{
    public string Provider { get; }
    public string? Collection { get; init; }
    public DataAdapterAttribute(string provider) => Provider = provider;
}
