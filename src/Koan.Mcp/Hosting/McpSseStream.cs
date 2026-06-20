using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Koan.Web.Sse;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 — one SSE stream within an <see cref="McpSession"/>: the session's single standalone server-push GET
/// stream, or a per-request POST response stream. Owns its own event-id sequence (a per-stream cursor that is
/// globally unique within the session because the stream id is embedded: <c>{streamId}.{seq}</c>) and a bounded
/// replay buffer, so a resumption GET carrying <c>Last-Event-ID</c> can replay this stream's tail — the MCP
/// Streamable HTTP resumability contract (the spec requires per-stream replay, never cross-stream).
/// </summary>
public sealed class McpSseStream
{
    /// <summary>The reserved id of the session's single standalone server-push GET stream.</summary>
    public const string GetStreamId = "GET";

    private readonly Channel<SseEnvelope> _channel = Channel.CreateUnbounded<SseEnvelope>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private readonly object _gate = new();
    private readonly LinkedList<(long Seq, SseEnvelope Envelope)> _replay = new();
    private readonly int _replayCapacity;
    private long _seq;

    internal McpSseStream(string id, int replayCapacity)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _replayCapacity = replayCapacity < 0 ? 0 : replayCapacity;
    }

    public string Id { get; }

    /// <summary>
    /// Enqueue a JSON-RPC message onto this stream as an <c>event: message</c> SSE frame, stamping the next
    /// event-id (<c>{streamId}.{seq}</c>) and buffering it for resumption. Returns the stamped event-id.
    /// </summary>
    public string EnqueueMessage(JObject message)
    {
        if (message is null) throw new ArgumentNullException(nameof(message));
        var seq = Interlocked.Increment(ref _seq);
        var eventId = FormatEventId(Id, seq);
        var envelope = new SseEnvelope("message", message.ToString(Newtonsoft.Json.Formatting.None), Id: eventId);
        Buffer(seq, envelope);
        _channel.Writer.TryWrite(envelope);
        return eventId;
    }

    /// <summary>Enqueue a raw frame (e.g. a keep-alive comment) WITHOUT an event-id — not replayable.</summary>
    public void EnqueueRaw(SseEnvelope envelope) => _channel.Writer.TryWrite(envelope);

    /// <summary>The live outbound frames for this stream (the SSE writer consumes this).</summary>
    public IAsyncEnumerable<SseEnvelope> Read(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>The buffered frames whose sequence is strictly greater than the one named by
    /// <paramref name="lastEventId"/> (an id that doesn't belong to this stream yields the whole buffer).</summary>
    public IReadOnlyList<SseEnvelope> ReplayAfter(string? lastEventId)
    {
        var afterSeq = TryParseSeq(lastEventId, Id);
        lock (_gate)
        {
            var result = new List<SseEnvelope>(_replay.Count);
            foreach (var (seq, env) in _replay)
            {
                if (seq > afterSeq) result.Add(env);
            }
            return result;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    private void Buffer(long seq, SseEnvelope envelope)
    {
        if (_replayCapacity == 0) return;
        lock (_gate)
        {
            _replay.AddLast((seq, envelope));
            while (_replay.Count > _replayCapacity) _replay.RemoveFirst();
        }
    }

    internal static string FormatEventId(string streamId, long seq)
        => string.Concat(streamId, ".", seq.ToString(CultureInfo.InvariantCulture));

    /// <summary>Extract the stream id from an event id of the form <c>{streamId}.{seq}</c> (null if malformed).</summary>
    public static string? StreamIdOf(string? eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return null;
        var dot = eventId.LastIndexOf('.');
        return dot <= 0 ? null : eventId[..dot];
    }

    private static long TryParseSeq(string? eventId, string streamId)
    {
        if (string.IsNullOrEmpty(eventId)) return 0;
        var dot = eventId.LastIndexOf('.');
        if (dot <= 0) return 0;
        if (!string.Equals(eventId[..dot], streamId, StringComparison.Ordinal)) return 0;
        return long.TryParse(eventId.AsSpan(dot + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq) ? seq : 0;
    }
}
