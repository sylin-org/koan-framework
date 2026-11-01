using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Core.Routing;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Default implementation of transaction coordinator factory.
/// </summary>
internal sealed class TransactionCoordinatorFactory : ITransactionCoordinatorFactory
{
    private readonly IDataService _dataService;
    private readonly ILogger<TransactionCoordinator> _logger;
    private readonly TransactionOptions _options;
    private readonly IServiceProvider _services;
    private readonly DataOperationHorizon _operationHorizon;
    private readonly DataProviderCatalog _providers;

    public TransactionCoordinatorFactory(
        IDataService dataService,
        IServiceProvider services,
        DataOperationHorizon operationHorizon,
        DataProviderCatalog providers,
        ILogger<TransactionCoordinator> logger,
        IOptions<TransactionOptions> options)
    {
        _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _operationHorizon = operationHorizon ?? throw new ArgumentNullException(nameof(operationHorizon));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public ITransactionCoordinator Create(string name, TransactionGuarantee guarantee = TransactionGuarantee.Coordinated)
        => Create(name, guarantee, isHeld: false);

    public ITransactionCoordinator CreateHeld(string name)
        => Create(name, TransactionGuarantee.Atomic, isHeld: true);

    private TransactionCoordinator Create(string name, TransactionGuarantee guarantee, bool isHeld)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Transaction name cannot be null or empty", nameof(name));

        return new TransactionCoordinator(
            name,
            _dataService,
            _services,
            _operationHorizon,
            _providers,
            _logger,
            _options,
            guarantee,
            isHeld);
    }
}
