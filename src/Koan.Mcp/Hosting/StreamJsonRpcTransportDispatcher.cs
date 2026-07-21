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

    public async Task Run(object target, Stream input, Stream output, CancellationToken cancellationToken)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (output is null) throw new ArgumentNullException(nameof(output));

        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions = SerializerOptions
        };
        using var handler = new NewLineDelimitedMessageHandler(output, input, formatter);
        using var rpc = new JsonRpc(handler, target);

        // RunContinuationsAsynchronously is load-bearing: TrySetResult/TrySetCanceled below can be raised
        // from inside JsonRpc.Dispose() (the Disconnected event), and we must NOT let the await-continuation
        // (which disposes the handler with a blocking wait) run inline on the disposing/cancelling thread —
        // that re-entrancy deadlocks shutdown. Schedule continuations off-thread instead.
        var completionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rpc.Disconnected += (_, args) =>
        {
            _logger.LogInformation("MCP JSON-RPC disconnected: {Reason}", args.Reason);
            completionSource.TrySetResult(null);
        };

        rpc.StartListening();

        using var registration = cancellationToken.Register(() =>
        {
            // Only SIGNAL cancellation here. Do not call rpc.Dispose() synchronously: Dispose() blocks
            // waiting for the read loop to drain, and that read loop can be parked in an uncancellable
            // console-stdin ReadFile — disposing on the cancellation thread would deadlock the canceller.
            // The `using` blocks below dispose rpc/handler in an orderly fashion once the read is unblocked
            // (the stdio transport closes stdin on stop to break the blocking read).
            _logger.LogInformation("Cancellation requested for MCP transport; ending session.");
            completionSource.TrySetCanceled(cancellationToken);
        });

        try
        {
            await completionSource.Task;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown; fall through to orderly disposal of the `using` instances.
        }
    }
}


