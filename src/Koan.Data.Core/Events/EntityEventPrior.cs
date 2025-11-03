using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Events;

/// <summary>
/// Lazily loads the prior snapshot for an entity lifecycle operation.
/// </summary>
/// <typeparam name="TEntity">Entity type.</typeparam>
public sealed class EntityEventPrior<TEntity> where TEntity : class
{
    private readonly Func<CancellationToken, ValueTask<TEntity?>> _loader;
    private readonly object _sync = new();
    private bool _loaded;
    private TEntity? _value;

    internal EntityEventPrior(Func<CancellationToken, ValueTask<TEntity?>> loader)
    {
        _loader = loader;
    }

    internal static EntityEventPrior<TEntity> Empty { get; } = new(_ => new ValueTask<TEntity?>(result: null));

    /// <summary>
    /// Gets a value indicating whether the snapshot has already been loaded.
    /// </summary>
    public bool IsLoaded => _loaded;

    /// <summary>
    /// Resolves the prior entity snapshot, if available.
    /// </summary>
    public ValueTask<TEntity?> Get(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return new ValueTask<TEntity?>(_value);
        }

        return LoadAsync(cancellationToken);
    }

    private async ValueTask<TEntity?> LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return _value;
        }

        var value = await _loader(cancellationToken);
        lock (_sync)
        {
            if (!_loaded)
            {
                _value = value;
                _loaded = true;
            }
        }

        return _value;
    }
}
