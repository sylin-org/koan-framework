namespace Koan.Core.Adapters;

public static class AdapterReadinessExtensions
{
    public static async Task<T> WithReadinessAsync<T>(this object adapter, Func<Task<T>> operation, CancellationToken ct = default)
    {
        if (adapter is not IAdapterReadiness readiness)
        {
            return await operation().ConfigureAwait(false);
        }

        if (adapter is IAdapterReadinessConfiguration configuration && configuration.EnableReadinessGating == false)
        {
            return await operation().ConfigureAwait(false);
        }

        var policy = (adapter as IAdapterReadinessConfiguration)?.Policy ?? ReadinessPolicy.Hold;

        switch (policy)
        {
            case ReadinessPolicy.Immediate:
                if (readiness.IsReady)
                {
                    break;
                }

                if (!await readiness.IsReadyAsync(ct).ConfigureAwait(false))
                {
                    throw new AdapterNotReadyException(adapter.GetType().Name, readiness.ReadinessState,
                        $"Adapter {adapter.GetType().Name} is not ready (State: {readiness.ReadinessState}).");
                }
                break;
            case ReadinessPolicy.Hold:
                var timeout = (adapter as IAdapterReadinessConfiguration)?.Timeout;
                await readiness.WaitForReadinessAsync(timeout, ct).ConfigureAwait(false);
                break;
            case ReadinessPolicy.Degrade:
                break;
        }

        return await operation().ConfigureAwait(false);
    }

    public static async Task WithReadinessAsync(this object adapter, Func<Task> operation, CancellationToken ct = default)
    {
        await adapter.WithReadinessAsync(async () =>
        {
            await operation().ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
    }
}
