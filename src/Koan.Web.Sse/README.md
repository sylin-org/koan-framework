# Sylin.Koan.Web.Sse

Return typed Server-Sent Events from a controller with one expression.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Sse
```

The package composes through the application's existing `AddKoan()` call.

## Smallest meaningful result

```csharp
[ApiController]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("updates")]
    public IActionResult Updates(CancellationToken cancellationToken)
        => Sse.Stream(OrderUpdates.Read(cancellationToken));
}
```

`Sse.Stream(...)` accepts any `IAsyncEnumerable<T>`:

- typed values become compact Newtonsoft JSON;
- strings remain unquoted text; and
- `SseEnvelope` values preserve explicit event name, id, retry, comment, and unnamed control frames.

The returned `SseResult` implements both MVC `IActionResult` and ASP.NET `IResult`, so Koan transports and application
controllers use the same wire engine without two helper vocabularies.

Configure only what differs from the defaults:

```json
{
  "Koan": {
    "Web": {
      "Sse": {
        "DefaultEvent": "message",
        "Heartbeat": "00:00:15"
      }
    }
  }
}
```

## A stream that lives

Every stream writes a `: heartbeat` comment once it has written nothing for `Heartbeat` (15 seconds; zero or less sends
none), so a proxy keeps it open while its source is quiet. `lifetime` ends it cleanly for the reader to reconnect, and
`Sse.LastEventId(Request)` is where a reconnecting reader says it had reached:

```csharp
[HttpGet("updates")]
public IActionResult Updates(SseHub<OrderUpdate> hub)
{
    if (hub.Open(User.Identity!.Name!, HttpContext.Abort) is not { } connection)
        return StatusCode(StatusCodes.Status429TooManyRequests);

    Response.RegisterForDispose(connection);
    return Sse.Stream(Frames(connection, Sse.LastEventId(Request)), lifetime: TimeSpan.FromMinutes(5));
}

private async IAsyncEnumerable<SseEnvelope> Frames(SseConnection<OrderUpdate> connection, string? after,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    using (connection)
    {
        foreach (var missed in await OrderUpdate.After(after, ct)) yield return missed.ToFrame();
        await foreach (var update in connection.Queue.ReadAllAsync(ct)) yield return update.ToFrame();
    }
}
```

Inject `SseHub<T>` and call `hub.Publish(value)` from wherever the value arises. It never waits: a stream whose queue is
full is closed and its request aborted, for its reader to reconnect and resume. A reader beyond `Hub:PerReader` (6) or a
stream beyond `Hub:InAll` (512) is refused by `Open` returning null, and `Hub:QueueBound` (256) sizes each queue.

## Guarantees and boundaries

- Responses use `text/event-stream`, add ordinary proxy buffering/caching guidance when absent, flush every frame,
  and stop on request cancellation. Existing stronger cache or application-owned transport directives survive.
- Typed and text values receive the explicit `Sse.Stream(..., eventName)` value or configured default. An explicit
  envelope remains unnamed unless the caller supplies `eventName`.
- An envelope with empty data plus a comment, retry, or id is a control frame. It emits only those control fields,
  even when the stream has a fallback event name; comment-only heartbeats are exactly `: text` plus the blank frame
  terminator.
- Empty text chunks are skipped; CR, LF and CRLF in data each begin a new `data:` line. An event name, id or comment
  holding a line break is refused with an exception, since it would forge another field.
- The package does not promise delivery, replay, resume storage, delivery across processes, authentication, or
  proxy-specific buffering behavior. Compose those concerns at their owning transport or host; `SseHub<T>` holds only
  what is queued in this process.

See [TECHNICAL.md](TECHNICAL.md) for projection and execution ownership.
