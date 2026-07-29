namespace Koan.Data.Core;

/// <summary>Declares operations and mappings owned by one named source.</summary>
public sealed class DataSourceBuilder
{
    private readonly string _source;

    internal DataSourceBuilder(string source) => _source = source;

    public DataSourceBuilder Map<TEntity>(Action<EntityMapBuilder<TEntity>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EntityMapBuilder<TEntity>(_source);
        configure(builder);
        Mapping.Composition.MappingDeclarationCatalog.Declare(builder.Build());
        return this;
    }

    public DataSourceBuilder Query(string name, Action<RecordQueryBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new RecordQueryBuilder(_source, name.Trim());
        configure(builder);
        DataOperationCatalog.Declare(builder.Build());
        return this;
    }

    public DataSourceBuilder Scalar<T>(string name, Action<ScalarQueryBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ScalarQueryBuilder(_source, name.Trim(), typeof(T));
        configure(builder);
        DataOperationCatalog.Declare(builder.Build());
        return this;
    }
}
