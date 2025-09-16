namespace Koan.Storage.Infrastructure;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class StorageBindingAttribute : Attribute
{
    public string? Profile { get; init; }
    public string? Container { get; init; }
    public StorageBindingAttribute() { }
    public StorageBindingAttribute(string? profile, string? container = null)
    {
        Profile = profile;
        Container = container;
    }
}
