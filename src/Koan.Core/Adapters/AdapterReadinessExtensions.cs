namespace Koan.Core.Adapters;

/// <summary>
/// Applies the framework-wide readiness policy before invoking an adapter operation.
/// Concern-specific recovery, such as data schema provisioning, composes around this boundary.
/// </summary>
public static class AdapterReadinessExtensions
{
    public static async Task<T> WithReadinessAsync<T>(
        this object adapter,
        Func<Task<T>> operation,
        CancellationToken ct = default)
    {
        if (adapter is not IAdapterReadiness readiness ||
            adapter is IAdapterReadinessConfiguration { EnableReadinessGating: false })
        {
            return await operation().ConfigureAwait(false);
        }

        var policy = (adapter as IAdapterReadinessConfiguration)?.Policy ?? ReadinessPolicy.Hold;
        switch (policy)
        {
            case ReadinessPolicy.Immediate:
                if (!readiness.IsReady && !await readiness.IsReadyAsync(ct).ConfigureAwait(false))
                {
                    throw new AdapterNotReadyException(
                        adapter.GetType().Name,
                        readiness.ReadinessState,
                        $"Adapter {adapter.GetType().Name} is not ready (State: {readiness.ReadinessState}).");
                }
                break;
            case ReadinessPolicy.Hold:
                await readiness.WaitForReadiness(
                    (adapter as IAdapterReadinessConfiguration)?.Timeout,
                    ct).ConfigureAwait(false);
                break;
            case ReadinessPolicy.Degrade:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported adapter readiness policy.");
        }

        return await operation().ConfigureAwait(false);
    }

    public static async Task WithReadiness(
        this object adapter,
        Func<Task> operation,
        CancellationToken ct = default)
    {
        await adapter.WithReadinessAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }
}
