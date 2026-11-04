using System;
using Koan.Core.Modules;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // Bind from configuration first, so a deployment can set the timeout and thresholds
        // without code. An explicit configure callback runs afterwards and therefore wins.
        services.AddKoanOptions<TransactionOptions>(TransactionOptions.SectionPath);
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Register factory, and beside it the committed heads of this host's sequences, which its holds raise (DATA-0169)
        services.AddSingleton<ITransactionCoordinatorFactory, TransactionCoordinatorFactory>();
        services.TryAddSingleton<SequenceHeads>();

        return services;
    }
}
