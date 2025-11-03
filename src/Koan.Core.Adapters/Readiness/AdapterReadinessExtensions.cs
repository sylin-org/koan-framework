using Koan.Data.Abstractions.Instructions;

namespace Koan.Core.Adapters;

public static class AdapterReadinessExtensions
{
    public static async Task<T> WithReadinessAsync<T>(this object adapter, Func<Task<T>> operation, CancellationToken ct = default)
    {
        if (adapter is not IAdapterReadiness readiness)
        {
            return await operation();
        }

        if (adapter is IAdapterReadinessConfiguration configuration && configuration.EnableReadinessGating == false)
        {
            return await operation();
        }

        var policy = (adapter as IAdapterReadinessConfiguration)?.Policy ?? ReadinessPolicy.Hold;

        switch (policy)
        {
            case ReadinessPolicy.Immediate:
                if (readiness.IsReady)
                {
                    break;
                }

                if (!await readiness.IsReadyAsync(ct))
                {
                    throw new AdapterNotReadyException(adapter.GetType().Name, readiness.ReadinessState,
                        $"Adapter {adapter.GetType().Name} is not ready (State: {readiness.ReadinessState}).");
                }
                break;
            case ReadinessPolicy.Hold:
                var timeout = (adapter as IAdapterReadinessConfiguration)?.Timeout;
                await readiness.WaitForReadinessAsync(timeout, ct);
                break;
            case ReadinessPolicy.Degrade:
                break;
        }

        // Execute operation with potential schema auto-provisioning
        return await ExecuteWithSchemaProvisioningAsync(adapter, operation, null, ct);
    }

    /// <summary>
    /// Enhanced version with automatic schema provisioning for entity operations.
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <typeparam name="TEntity">Entity type for schema provisioning context</typeparam>
    /// <param name="adapter">The data adapter</param>
    /// <param name="operation">Operation to execute</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Operation result</returns>
    public static async Task<T> WithReadinessAsync<T, TEntity>(this object adapter, Func<Task<T>> operation, CancellationToken ct = default)
        where TEntity : class
    {
        if (adapter is not IAdapterReadiness readiness)
        {
            return await ExecuteWithSchemaProvisioningAsync<T, TEntity>(adapter, operation, ct);
        }

        if (adapter is IAdapterReadinessConfiguration configuration && configuration.EnableReadinessGating == false)
        {
            return await ExecuteWithSchemaProvisioningAsync<T, TEntity>(adapter, operation, ct);
        }

        var policy = (adapter as IAdapterReadinessConfiguration)?.Policy ?? ReadinessPolicy.Hold;

        switch (policy)
        {
            case ReadinessPolicy.Immediate:
                if (readiness.IsReady)
                {
                    break;
                }

                if (!await readiness.IsReadyAsync(ct))
                {
                    throw new AdapterNotReadyException(adapter.GetType().Name, readiness.ReadinessState,
                        $"Adapter {adapter.GetType().Name} is not ready (State: {readiness.ReadinessState}).");
                }
                break;
            case ReadinessPolicy.Hold:
                var timeout = (adapter as IAdapterReadinessConfiguration)?.Timeout;
                await readiness.WaitForReadinessAsync(timeout, ct);
                break;
            case ReadinessPolicy.Degrade:
                break;
        }

        // Execute operation with schema auto-provisioning for entity type
        return await ExecuteWithSchemaProvisioningAsync<T, TEntity>(adapter, operation, ct);
    }

    private static async Task<T> ExecuteWithSchemaProvisioningAsync<T, TEntity>(object adapter, Func<Task<T>> operation, CancellationToken ct)
        where TEntity : class
    {
        return await ExecuteWithSchemaProvisioningAsync(adapter, operation, typeof(TEntity), ct);
    }

    private static async Task<T> ExecuteWithSchemaProvisioningAsync<T>(object adapter, Func<Task<T>> operation, Type? entityType, CancellationToken ct)
    {
        try
        {
            // First attempt: Execute operation normally
            return await operation();
        }
        catch (Exception ex) when (entityType != null && IsSchemaRelatedFailure(ex))
        {
            // Schema failure detected - attempt auto-provisioning if supported
            var executorType = typeof(IInstructionExecutor<>).MakeGenericType(entityType);
            if (executorType.IsInstanceOfType(adapter))
            {
                try
                {
                    // Execute EnsureCreated instruction using reflection
                    var executeMethod = executorType.GetMethod("ExecuteAsync")?.MakeGenericMethod(typeof(bool));
                    if (executeMethod != null)
                    {
                        var instruction = new Instruction(DataInstructions.EnsureCreated);
                        var task = (Task<bool>?)executeMethod.Invoke(adapter, new object[] { instruction, ct });
                        if (task != null)
                        {
                            await task;
                        }
                    }

                    // Retry the operation after schema provisioning
                    return await operation();
                }
                catch (Exception provisioningEx)
                {
                    // If provisioning fails, surface the provisioning failure with original context attached
                    throw new InvalidOperationException(
                        $"Schema auto-provisioning failed for {entityType.Name}. Original error: {ex.Message}",
                        provisioningEx);
                }
            }

            // Re-throw original exception if auto-provisioning not supported
            throw;
        }
    }

    /// <summary>
    /// Determines if an exception is related to missing schema/structure.
    /// </summary>
    private static bool IsSchemaRelatedFailure(Exception ex)
    {
        var message = ex.Message?.ToLowerInvariant() ?? string.Empty;

        // Common schema-related failure patterns across providers
        return message.Contains("keyspace not found") ||           // Couchbase
               message.Contains("collection does not exist") ||     // Various NoSQL
               message.Contains("table") && message.Contains("does not exist") ||  // SQL
               message.Contains("invalid object name") ||           // SQL Server
               message.Contains("relation") && message.Contains("does not exist") || // PostgreSQL
               message.Contains("no such table") ||                // SQLite
               message.Contains("unknown collection") ||           // MongoDB
               message.Contains("index not found") ||              // Vector stores
               (ex.GetType().Name.Contains("Schema") && message.Contains("not found"));
    }

    public static async Task WithReadinessAsync(this object adapter, Func<Task> operation, CancellationToken ct = default)
    {
        await adapter.WithReadinessAsync(async () =>
        {
            await operation();
            return true;
        }, ct);
    }
}
