using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Semantics;
using Koan.Data.Relational;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed class SqliteEntityPlan<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;
    private readonly JsonSerializerSettings _json;

    public SqliteEntityPlan(IServiceProvider services)
    {
        _services = services;
        Optimization = services.GetStorageOptimization<TEntity, TKey>();
        var segmentation = services.GetRequiredService<DataSegmentationPlan>().For(typeof(TEntity));
        _json = ComparableScalarEncoding.Apply(new JsonSerializerSettings(), segmentation.Fields);
        IdentityName = Optimization.IdPropertyName;
    }

    public StorageOptimizationInfo Optimization { get; }
    public string IdentityName { get; }
    public string Table => Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(_services);

    public string Key(TKey key) => key.ToString()
        ?? throw new InvalidOperationException($"Entity key '{typeof(TKey).FullName}' produced no storage value.");

    public (string Id, string Json) Write(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return (Key(entity.Id), JsonConvert.SerializeObject(entity, entity.GetType(), _json));
    }

    public TEntity Read(string json) =>
        (TEntity)(JsonConvert.DeserializeObject(json, typeof(TEntity), _json)
            ?? throw new InvalidDataException($"SQLite returned empty JSON for '{typeof(TEntity).FullName}'."));
}
