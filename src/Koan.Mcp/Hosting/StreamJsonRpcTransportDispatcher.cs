using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Koan.Mcp.Hosting;

public sealed class StreamJsonRpcTransportDispatcher : IMcpTransportDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILogger<StreamJsonRpcTransportDispatcher> _logger;

    public StreamJsonRpcTransportDispatcher(ILogger<StreamJsonRpcTransportDispatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(object target, Stream input, Stream output, CancellationToken cancellationToken)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions = SerializerOptions
        };
        using var handler = new HeaderDelimitedMessageHandler(output, input, formatter);
        using var rpc = new JsonRpc(handler, target);

        var completionSource = new TaskCompletionSource<object?>();
        rpc.Disconnected += (_, args) =>
        {
            _logger.LogInformation("MCP JSON-RPC disconnected: {Reason}", args.Reason);
            completionSource.TrySetResult(null);
        };

        rpc.StartListening();

        using var registration = cancellationToken.Register(() =>
        {
            _logger.LogInformation("Cancellation requested for MCP transport; disposing JSON-RPC instance.");
            rpc.Dispose();
            completionSource.TrySetCanceled(cancellationToken);
        });

        await completionSource.Task;
    }
}


