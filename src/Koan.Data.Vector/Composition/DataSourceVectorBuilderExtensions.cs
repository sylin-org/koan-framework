using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Data.Vector;

public static class DataSourceVectorBuilderExtensions
{
    /// <summary>Declares one source-owned vector space for an Entity.</summary>
    public static DataSourceBuilder Vector<TEntity>(
        this DataSourceBuilder source,
        Action<VectorSpaceBuilder<TEntity>> configure)
        where TEntity : class, IEntity<string>
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new VectorSpaceBuilder<TEntity>();
        configure(builder);
        VectorSpaceDeclarationCatalog.Declare(typeof(TEntity), builder.Build(source.Name));
        return source;
    }
}
