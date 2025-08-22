namespace Sora.Messaging;

[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class PartitionKeyAttribute : Attribute { }