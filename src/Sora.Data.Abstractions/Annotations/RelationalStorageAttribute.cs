namespace Sora.Data.Abstractions.Annotations;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RelationalStorageAttribute : Attribute
{
    public RelationalStorageShape Shape { get; init; } = RelationalStorageShape.Json;
}
