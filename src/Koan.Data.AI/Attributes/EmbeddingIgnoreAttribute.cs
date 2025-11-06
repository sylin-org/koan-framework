namespace Koan.Data.AI.Attributes;

/// <summary>
/// Marks a property to be excluded from automatic embedding text generation.
/// Only applies when using Policy-based auto-discovery (AllStrings or AllPublic).
/// Has no effect when using explicit Properties list or Template.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class EmbeddingIgnoreAttribute : Attribute
{
}
