namespace Koan.Data.Abstractions.Annotations;

/// <summary>
/// Declares the configured data source an entity lives in. Every operation on the entity routes to
/// <c>Koan:Data:Sources:{name}</c>, which must declare an <c>Adapter</c>; an unconfigured name is refused on the first
/// operation. An explicit <c>EntityContext.Source(...)</c> and a Database-mode data axis still take precedence. The
/// entity is outside default-route promotion (DATA-0127).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DataSourceAttribute : Attribute
{
    public string Name { get; }
    public DataSourceAttribute(string name) => Name = name;
}
