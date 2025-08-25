namespace Sora.Messaging;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class MessageAttribute : Attribute
{
    public string? Alias { get; set; }
    public int Version { get; set; } = 1;
    public string? Bus { get; set; }
    public string? Group { get; set; }
}