namespace Sora.Data.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class SourceAdapterAttribute : Attribute
{
    public string Provider { get; }
    public SourceAdapterAttribute(string provider) => Provider = provider;
}
