namespace Sora.Data.Abstractions;

// Used to influence default provider selection when multiple IDataAdapterFactory instances exist.
// Higher Priority wins. If missing, Priority defaults to 0.
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ProviderPriorityAttribute : Attribute
{
    public int Priority { get; }
    public ProviderPriorityAttribute(int priority) => Priority = priority;
}
