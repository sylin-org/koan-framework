using System;

namespace Sora.Data.Abstractions.Annotations;

public enum RelationalStorageShape
{
    Json,
    ComputedProjections,
    PhysicalColumns
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RelationalStorageAttribute : Attribute
{
    public RelationalStorageShape Shape { get; init; } = RelationalStorageShape.Json;
}
