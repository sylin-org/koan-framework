using System;

namespace Sora.Data.Abstractions.Annotations;

/// <summary>
/// Declares a logical data source name for an entity. The selected provider can use this name
/// to resolve its connection string from configuration (e.g., ConnectionStrings:{name} or Sora:Data:Sources:{name}:{provider}).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DataSourceAttribute : Attribute
{
    public string Name { get; }
    public DataSourceAttribute(string name) => Name = name;
}
