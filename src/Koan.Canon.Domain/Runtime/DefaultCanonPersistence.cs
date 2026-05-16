using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Model;
using Koan.Data.Core;

namespace Koan.Canon.Domain.Runtime;

/// <summary>
/// Default persistence strategy that relies on Koan entity helpers.
/// </summary>
internal sealed class DefaultCanonPersistence : ICanonPersistence
{
    public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
        where TModel : CanonEntity<TModel>, new()
        => entity.Save(cancellationToken);

    public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
        where TModel : CanonEntity<TModel>, new()
        => stage.Save(cancellationToken);

    public async Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
    {
        var matches = await CanonIndex.Query(index => index.EntityType == entityType && index.Key == key, cancellationToken);
        return matches.FirstOrDefault();
    }

    public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
        => CanonIndex.Upsert(index, cancellationToken);
}
