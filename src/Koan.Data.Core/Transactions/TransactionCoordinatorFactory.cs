using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Default implementation of transaction coordinator factory.
/// </summary>
internal sealed class TransactionCoordinatorFactory : ITransactionCoordinatorFactory
{
    private readonly IDataService _dataService;
    private readonly ILogger<TransactionCoordinator> _logger;
    private readonly TransactionOptions _options;

    public TransactionCoordinatorFactory(
        IDataService dataService,
        ILogger<TransactionCoordinator> logger,
        IOptions<TransactionOptions> options)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public ITransactionCoordinator Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Transaction name cannot be null or empty", nameof(name));

        return new TransactionCoordinator(name, _dataService, _logger, _options);
    }
}
