namespace Koan.Data.Core.Lifecycle;

/// <summary>Lazily resolves the persisted value that preceded a lifecycle operation.</summary>
public sealed class EntityLifecyclePrior<TEntity>(Func<CancellationToken, ValueTask<TEntity?>> loader)
    where TEntity : class
{
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private bool _loaded;
    private TEntity? _value;

    internal static EntityLifecyclePrior<TEntity> Empty { get; } =
        new(_ => new ValueTask<TEntity?>((TEntity?)null));

    public bool IsLoaded => _loaded;

    public async ValueTask<TEntity?> Get(CancellationToken cancellationToken = default)
    {
        if (_loaded) return _value;

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded)
            {
                _value = await loader(cancellationToken).ConfigureAwait(false);
                _loaded = true;
            }

            return _value;
        }
        finally
        {
            _loadGate.Release();
        }
    }
}
