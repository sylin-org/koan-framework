using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Core.Logging;

internal sealed class KoanLogFactoryBridge : IHostedService
{
    private readonly ILoggerFactory _factory;

    public KoanLogFactoryBridge(ILoggerFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        KoanLog.AttachFactory(_factory);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        KoanLog.DetachFactory(_factory);
        return Task.CompletedTask;
    }
}
