# Sylin.Koan.Web.Sse technical contract

## Responsibility

`SseModule` binds `KoanSseOptions`, registers the open-generic `SseHub<>` singleton, and reports the actual runtime
posture. `Sse` owns value-to-envelope projection and reading `Last-Event-ID`. `SseResult` owns HTTP headers, fallback
event normalization, formatting, cancellation, writing, per-frame flushing, the heartbeat and the lifetime.
`SseFormatter` owns wire safety and multiline fields. `SseHub<T>` owns admission and bounded fan-out in one process.

The same `SseResult` implements MVC `IActionResult` and ASP.NET `IResult`. This is intentional framework
infrastructure support, not guidance to define application routes with minimal-API mapping; Koan applications remain
controller-first.

## Projection

The generic `Sse.Stream<T>` path inspects each yielded value:

1. `SseEnvelope` passes through with all explicit wire fields;
2. `string` becomes raw SSE data and empty strings are skipped; and
3. every other value is serialized with compact Newtonsoft JSON.

The result applies its explicit event name as a fallback. `KoanSseOptions.DefaultEvent` applies to typed and text
projection only; an explicit unnamed envelope remains unnamed so comments and other control frames retain their
protocol meaning. Explicit envelope event names always win.

An envelope with empty data and at least one comment, retry, or id field is a control frame. Formatting suppresses
both event and data fields for that frame, writes the supplied control fields, and terminates with one blank line.
An empty envelope without control fields remains an empty data event. Null data remains invalid.

CR, LF and CRLF in data each begin a new `data:` line, since a reader's parser ends a line at any of them. A line break
in an event name, id or comment throws `ArgumentException`: it would end the field and begin whatever follows as
another, forging a frame from text. The throw ends the stream and surfaces, as any source failure does.

## Execution

Execution resolves the host-owned `IOptions<KoanSseOptions>`, composes the SSE response headers, enumerates with a token
linked to `HttpContext.RequestAborted`, formats each envelope, writes it, and flushes the response body. There is one
execution path for AI streaming, MCP HTTP transports, application controllers, and tests.

The source's read is held across waits rather than cancelled, so a quiet source loses nothing: when `Heartbeat` has
passed since the last write, whatever was written, a `: heartbeat` comment goes out and the same read is waited on again.
`lifetime` ends the stream with a normal completion, for the reader to reconnect. However the stream ends, the source's
token is cancelled, its read is awaited and its enumerator disposed. A failure of the source or a write surfaces.

Cache control is additive: absent policy becomes `no-cache`, an existing weaker policy gains `no-cache`, and an
existing `no-store`/`no-cache` policy is preserved. `Pragma`, `Connection`, and `X-Accel-Buffering` are set only when
the application or middleware has not already made that decision.

## Fan-out

`SseHub<T>` admits a stream through `Open(reader, abort)`, counted under one lock and refused with null beyond
`Hub:PerReader` for that reader or `Hub:InAll` in all; these and `Hub:QueueBound` size memory, and a value below 1 refuses
to construct. `Publish` walks the open connections and writes to each bounded channel without waiting. A full channel
closes that connection alone: its queue completes, its place returns and `abort` ends its request, so one slow reader
costs no other reader anything. `Dispose` gives the place back, once. The hub keeps nothing past its queues and spans one
process; an application replays from its own record on reconnect, and a fleet delivers to each process's hub.

## Deliberate non-guarantees

- no replay buffer or `Last-Event-ID` persistence;
- no delivery across processes;
- no durable delivery or retry policy;
- no authentication/authorization policy; and
- no promise that an external proxy honors buffering headers.
