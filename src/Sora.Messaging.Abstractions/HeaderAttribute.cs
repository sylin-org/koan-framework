namespace Sora.Messaging;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class HeaderAttribute : Attribute
{
    public HeaderAttribute(string name) { Name = name; }
    public string Name { get; }
}