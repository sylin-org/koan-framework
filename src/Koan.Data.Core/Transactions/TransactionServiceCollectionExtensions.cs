using System;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Extension methods for registering transaction support in DI.
/// </summary>
public static class TransactionServiceCollectionExtensions
{
    /// <summary>
    /// Enable transaction support for Entity operations.
    /// After calling this, EntityContext.Transaction() becomes available.
    /// </summary>
    public static IServiceCollection AddKoanTransactions(
        this IServiceCollection services,
        Action<TransactionOptions>? configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Register options
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<TransactionOptions>(options => { });
        }

        // Register factory
        services.AddSingleton<ITransactionCoordinatorFactory, TransactionCoordinatorFactory>();

        return services;
    }
}
