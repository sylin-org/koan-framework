namespace Sora.Data.Vector.Abstractions;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class VectorEmbeddingAttribute : Attribute
{
    public string? Name { get; }
    public VectorEmbeddingAttribute(string? name = null) => Name = name;
}