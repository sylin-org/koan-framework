using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Execution;
using Microsoft.Extensions.Logging;

namespace Koan.Mcp.Hosting;

public sealed class McpServer
{
    private readonly McpEntityRegistry _registry;
    private readonly EndpointToolExecutor _executor;
    private readonly IMcpTransportDispatcher _dispatcher;
    private readonly ILoggerFactory _loggerFactory;

    public McpServer(
        McpEntityRegistry registry,
        EndpointToolExecutor executor,
        IMcpTransportDispatcher dispatcher,
        ILoggerFactory loggerFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public IReadOnlyList<McpEntityRegistration> GetRegistrationsForStdio()
        => _registry.RegistrationsForStdio();

    public IReadOnlyList<McpEntityRegistration> GetRegistrationsForHttpSse()
        => _registry.RegistrationsForHttpSse();

    public McpRpcHandler CreateHandler()
    {
        var handlerLogger = _loggerFactory.CreateLogger<McpRpcHandler>();
        return new McpRpcHandler(_registry, _executor, handlerLogger);
    }

    public Task RunAsync(McpRpcHandler handler, Stream input, Stream output, CancellationToken cancellationToken)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));

        return _dispatcher.RunAsync(handler, input, output, cancellationToken);
    }
}
