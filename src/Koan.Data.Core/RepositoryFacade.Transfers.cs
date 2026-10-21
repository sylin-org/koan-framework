using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Transfers;

namespace Koan.Data.Core;

/// <summary>The transfer's view of the delete-override plane (DATA-0155). See <see cref="ITransferRows{TEntity,TKey}"/>.</summary>
internal sealed partial class RepositoryFacade<TEntity, TKey> : ITransferRows<TEntity, TKey>
{
    bool ITransferRows<TEntity, TKey>.HasDeleteOverride => _deleteOverride is not null;

    async Task<IReadOnlySet<TKey>> ITransferRows<TEntity, TKey>.Marked(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (_deleteOverride is not { } mark || ids.Count == 0) return new HashSet<TKey>();
        await using var operation = await Guard(DataOperationEffect.Read, "entity transfer marked read", ct);
        var marked = Filter.All(
            Filter.In(_idField, ids.Cast<object?>().ToList()),
            Filter.On(FieldPath.Of(mark.Field), FilterOperator.Eq, FilterValue.Of(mark.OnDeleteValue)));
        var scope = ReadScopeFilter(operation.Segmentation);
        var rows = await RequireQuery().Query(
            QueryDefinition.All.Where(scope is null ? marked : Filter.All(marked, scope)), ct);
        return rows.Items.Select(static row => row.Id).ToHashSet();
    }

    async Task<int> ITransferRows<TEntity, TKey>.UpsertMarked(IReadOnlyList<TEntity> rows, CancellationToken ct)
    {
        var mark = _deleteOverride
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' has no delete override to mark rows with.");
        await using var operation = await Guard(DataOperationEffect.Write, "entity transfer marked write", ct);
        var overrides = new Dictionary<string, object?>(StringComparer.Ordinal) { [mark.Field] = mark.OnDeleteValue };
        var values = CurrentManagedValues(operation.Segmentation);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            WritePlanFor(row).ApplyAll(row);
            var payload = WritePayload(row);
            try
            {
                if (values is null)
                    using (ManagedFieldWriteScope.EnterOverrides(overrides)) await _inner.Upsert(payload, ct);
                else
                    using (ManagedFieldWriteScope.Enter(values, overrides)) await _inner.Upsert(payload, ct);
            }
            catch (Exception error) when (UniqueConflict(error, out var conflict)) { throw conflict; }
        }
        return rows.Count;
    }

    async Task<int> ITransferRows<TEntity, TKey>.Purge(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        await using var operation = await Guard(DataOperationEffect.Write, "entity transfer purge", ct);
        return await PhysicalDeleteMany(ids, ReadScopeFilter(operation.Segmentation), ct);
    }
}
