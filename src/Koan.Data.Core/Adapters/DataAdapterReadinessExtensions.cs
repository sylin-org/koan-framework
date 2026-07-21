using Koan.Core.Adapters;
using Koan.Data.Abstractions.Instructions;

namespace Koan.Data.Adapters;

/// <summary>
/// Composes data-owned schema recovery around the framework-wide adapter readiness gate.
/// </summary>
public static class DataAdapterReadinessExtensions
{
    public static Task<T> WithDataReadinessAsync<T, TEntity>(
        this object adapter,
        Func<Task<T>> operation,
        CancellationToken ct = default)
        where TEntity : class =>
        adapter.WithReadinessAsync(
            () => ExecuteWithSchemaProvisioningAsync<T, TEntity>(adapter, operation, ct),
            ct);

    private static async Task<T> ExecuteWithSchemaProvisioningAsync<T, TEntity>(
        object adapter,
        Func<Task<T>> operation,
        CancellationToken ct)
        where TEntity : class
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsSchemaRelatedFailure(ex))
        {
            var executorType = typeof(IInstructionExecutor<>).MakeGenericType(typeof(TEntity));
            if (!executorType.IsInstanceOfType(adapter))
            {
                throw;
            }

            try
            {
                var executeMethod = executorType.GetMethod("Execute")?.MakeGenericMethod(typeof(bool));
                if (executeMethod is not null)
                {
                    var instruction = new Instruction(DataInstructions.EnsureCreated);
                    if (executeMethod.Invoke(adapter, [instruction, ct]) is Task<bool> task)
                    {
                        await task.ConfigureAwait(false);
                    }
                }

                return await operation().ConfigureAwait(false);
            }
            catch (Exception provisioningException)
            {
                throw new InvalidOperationException(
                    $"Schema auto-provisioning failed for {typeof(TEntity).Name}. Original error: {ex.Message}",
                    provisioningException);
            }
        }
    }

    private static bool IsSchemaRelatedFailure(Exception exception)
    {
        var message = exception.Message?.ToLowerInvariant() ?? "";
        return message.Contains("keyspace not found") ||
               message.Contains("collection does not exist") ||
               message.Contains("table") && message.Contains("does not exist") ||
               message.Contains("invalid object name") ||
               message.Contains("relation") && message.Contains("does not exist") ||
               message.Contains("no such table") ||
               message.Contains("unknown collection") ||
               message.Contains("index not found") ||
               exception.GetType().Name.Contains("Schema") && message.Contains("not found");
    }
}
