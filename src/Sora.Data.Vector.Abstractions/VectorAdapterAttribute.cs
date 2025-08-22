namespace Sora.Data.Vector.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class VectorAdapterAttribute : Attribute
{
    public string Provider { get; }
    public VectorAdapterAttribute(string provider) => Provider = provider;
}