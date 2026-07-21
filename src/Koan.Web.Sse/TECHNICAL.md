# Sylin.Koan.Web.Sse technical contract

## Responsibility

`SseModule` binds the one effective setting and reports the actual runtime posture. `Sse` owns value-to-envelope
projection. `SseResult` owns HTTP headers, fallback event normalization, formatting, cancellation, writing, and
per-frame flushing. `SseFormatter` owns wire escaping and multiline fields.

The same `SseResult` implements MVC `IActionResult` and ASP.NET `IResult`. This is intentional framework
infrastructure support, not guidance to define application routes with minimal-API mapping; Koan applications remain
controller-first.

## Projection

The generic `Sse.Stream<T>` path inspects each yielded value:

1. `SseEnvelope` passes through with all explicit wire fields;
2. `string` becomes raw SSE data and empty strings are skipped; and
3. every other value is serialized with compact Newtonsoft JSON.

The result applies its explicit event name as a fallback, then `KoanSseOptions.DefaultEvent`. Explicit envelope event
names always win.

## Execution

Execution resolves the host-owned `IOptions<KoanSseOptions>`, sets the SSE response headers, enumerates with
`HttpContext.RequestAborted`, formats each envelope, writes it, and flushes the response body. There is one execution
path for AI streaming, MCP HTTP transports, application controllers, and tests.

## Deliberate non-guarantees

- no heartbeat scheduler;
- no replay buffer or `Last-Event-ID` persistence;
- no durable delivery or retry policy;
- no authentication/authorization policy; and
- no promise that an external proxy honors buffering headers.
