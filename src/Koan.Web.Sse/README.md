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

Configure only the fallback event name when needed:

```json
{
  "Koan": {
    "Web": {
      "Sse": {
        "DefaultEvent": "message"
      }
    }
  }
}
```

## Guarantees and boundaries

- Responses use `text/event-stream`, add ordinary proxy buffering/caching guidance when absent, flush every frame,
  and stop on request cancellation. Existing stronger cache or application-owned transport directives survive.
- Typed and text values receive the explicit `Sse.Stream(..., eventName)` value or configured default. An explicit
  envelope remains unnamed unless the caller supplies `eventName`.
- Empty text chunks are skipped; multiline data is emitted as valid repeated `data:` lines.
- The package does not promise delivery, replay, resume storage, heartbeat generation, backpressure persistence,
  authentication, or proxy-specific buffering behavior. Compose those concerns at their owning transport or host.

See [TECHNICAL.md](TECHNICAL.md) for projection and execution ownership.
