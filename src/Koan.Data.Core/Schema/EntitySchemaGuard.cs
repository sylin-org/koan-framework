using Koan.Core.Infrastructure;
using Koan.Core.Logging;
using Koan.Data.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Schema;

/// <summary>
/// Coordinates schema provisioning for an entity across adapters and logical sets.
/// </summary>
internal sealed class EntitySchemaGuard<TEntity, TKey>
    where TEntity : class, Koan.Data.Abstractions.IEntity<TKey>
    where TKey : notnull
{
    private static readonly ConcurrentDictionary<string, ProvisionState> _states = new(StringComparer.Ordinal);

    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private ISchemaHealthContributor<TEntity, TKey>? _contributor;
    private bool _attempted;

    private record ProvisionState(
        bool IsProvisioned,
        DateTime? ProvisionedAt,
        ProvisionError? Error);

    private record ProvisionError(
        string Message,
        DateTime FailedAt,
        int AttemptCount);

    public EntitySchemaGuard(IServiceProvider services, ILogger<EntitySchemaGuard<TEntity, TKey>>? logger)
    {
        _services = services;
        _logger = logger ?? NullLogger<EntitySchemaGuard<TEntity, TKey>>.Instance;
    }

    /// <summary>
    /// Ensures the entity's backing store is healthy for the current dataset.
    /// </summary>
    public async Task EnsureHealthyAsync(CancellationToken ct)
    {
        var contributor = GetContributor();
        if (contributor is null)
        {
            return;
        }

        var storageKey = BuildStorageKey();

        // Check provisioning state and enforce error retry backoff
        if (_states.TryGetValue(storageKey, out var state))
        {
            if (state.IsProvisioned) return;

            if (state.Error != null)
            {
                // Allow retry after 5 minutes
                var elapsed = DateTime.UtcNow - state.Error.FailedAt;
                if (elapsed < TimeSpan.FromMinutes(5))
                {
                    var remaining = TimeSpan.FromMinutes(5) - elapsed;
                    throw new InvalidOperationException(
                        $"Provisioning failed for {typeof(TEntity).Name} on {storageKey}. " +
                        $"Retry in {remaining:mm\\:ss} (attempt #{state.Error.AttemptCount})");
                }
            }
        }

        await Singleflight.RunAsync(storageKey, async token =>
        {
            if (_states.TryGetValue(storageKey, out var hot) && hot.IsProvisioned)
            {
                return;
            }

            try
            {
                await contributor.EnsureHealthyAsync(token).ConfigureAwait(false);
                _states[storageKey] = new ProvisionState(true, DateTime.UtcNow, null);
                KoanLog.DataDebug(_logger, LogActions.SchemaEnsure, "healthy",
                    ("entity", typeof(TEntity).FullName ?? typeof(TEntity).Name),
                    ("storage", storageKey));
            }
            catch (Exception ex)
            {
                var attemptCount = state?.Error?.AttemptCount ?? 0;
                var error = new ProvisionError(ex.Message, DateTime.UtcNow, attemptCount + 1);
                _states[storageKey] = new ProvisionState(false, null, error);
                KoanLog.DataDebug(_logger, LogActions.SchemaEnsure, "failed",
                    ("entity", typeof(TEntity).FullName ?? typeof(TEntity).Name),
                    ("storage", storageKey),
                    ("attempt", attemptCount + 1),
                    ("error", ex.Message));
                throw;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Invalidates the cached health state, forcing the next ensure attempt to run again.
    /// </summary>
    public void Invalidate()
    {
        var contributor = GetContributor();
        if (contributor is null)
        {
            return;
        }

        var storageKey = BuildStorageKey();
        _states.TryRemove(storageKey, out _);
        contributor.InvalidateHealth();
        Singleflight.Invalidate(storageKey);
        KoanLog.DataDebug(_logger, LogActions.SchemaEnsure, "invalidated",
            ("entity", typeof(TEntity).FullName ?? typeof(TEntity).Name),
            ("storage", storageKey));
    }

    /// <summary>
    /// Clears provisioning error for manual retry (static helper for diagnostics).
    /// </summary>
    public void ClearProvisioningError()
    {
        var storageKey = BuildStorageKey();
        _states.TryRemove(storageKey, out _);
        KoanLog.DataDebug(_logger, LogActions.SchemaEnsure, "error-cleared",
            ("entity", typeof(TEntity).FullName ?? typeof(TEntity).Name),
            ("storage", storageKey));
    }

    private ISchemaHealthContributor<TEntity, TKey>? GetContributor()
    {
        if (_attempted)
        {
            return _contributor;
        }

        _attempted = true;
        _contributor = _services.GetService<ISchemaHealthContributor<TEntity, TKey>>();
        return _contributor;
    }

    private string BuildStorageKey()
    {
        var sourceRegistry = _services.GetRequiredService<DataSourceRegistry>();
        var (adapter, source) = AdapterResolver.ResolveForEntity<TEntity>(_services, sourceRegistry);
        var partition = EntityContext.Current?.Partition ?? "root";
        return $"{adapter}:{source}:{partition}";
    }

    private static class LogActions
    {
        public const string SchemaEnsure = "schema.ensure";
    }
}
