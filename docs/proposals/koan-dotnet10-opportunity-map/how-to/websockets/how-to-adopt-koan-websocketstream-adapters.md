# How to Adopt Koan WebSocketStream Adapters

**Contract**

- **Inputs**: ASP.NET Core host using `services.AddKoanAutoRegistrars()`, WebSocket-capable clients, optional configuration under `Koan:Web:WebSockets`.
- **Outputs**: Bidirectional `WebSocketStream` pipeline for controllers and background workers, provenance entries describing negotiated protocol shape.
- **Success When**: Hosts accept upgrades through `HttpContext.AcceptWebSocketStreamAsync()`, DI resolves `IWebSocketStreamFactory`, and telemetry reports configured message framing.
- **Failure When**: WebSocket upgrades are not negotiated, stream disposal closes required sockets unexpectedly, or provenance omits adapter settings.

## Edge Cases

- Clients requesting sub-protocols not whitelisted by your configuration path.
- Large binary frames that benefit from paging or chunking before writing to the stream.
- Downgraded environments where WebSockets are disabled by reverse proxies.
- Hosts that require the underlying `WebSocket` to remain open after stream disposal (`LeaveOpen=true`).
- Legacy callers emitting text frames where the server expects binary payloads.

## Prerequisites

- Reference the `Koan.WebSockets` package from your application.
- Keep the default Koan bootstrap (`services.AddKoanAutoRegistrars()`) so the module self-registers; opt-out hosts must call `services.AddWebSocketStreamAdapters()` directly.
- Ensure reverse proxies and gateways forward WebSocket upgrade headers.

## Service Registration

Koan auto-registration binds `WebSocketStreamOptions` from `Koan:Web:WebSockets` and exposes `IWebSocketStreamFactory` for DI consumers. Manual hosts can register explicitly:

```csharp
public static class Hosting
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddWebSocketStreamAdapters();
    }
}
```

### Configuration knobs

Set defaults in `appsettings.json` when the built-in defaults (`Binary`, `LeaveOpen=false`) are undesirable.

```json
{
  "Koan": {
    "Web": {
      "WebSockets": {
        "MessageType": "Text",
        "LeaveOpen": true,
        "SubProtocol": "chat.v2"
      }
    }
  }
}
```

## Accepting Upgrades in Controllers

Use the provided `HttpContext.AcceptWebSocketStreamAsync()` helper to turn an HTTP upgrade into a `Stream` you can compose with existing pipelines.

```csharp
[Route("streaming/chat")]
public sealed class ChatStreamController : ControllerBase
{
  private readonly IWebSocketStreamFactory _streams;

  public ChatStreamController(IWebSocketStreamFactory streams)
  {
    _streams = streams;
  }

  [HttpGet]
  public async Task Get(CancellationToken cancellationToken)
  {
    await using var socketStream = await HttpContext.AcceptWebSocketStreamAsync(cancellationToken: cancellationToken);

    using var writer = new StreamWriter(socketStream, leaveOpen: true);
    await writer.WriteLineAsync("connected", cancellationToken);

    var buffer = new byte[4096];
    var read = await socketStream.ReadAsync(buffer, cancellationToken);
    await socketStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
  }
}
```

- `AcceptWebSocketStreamAsync` negotiates the upgrade, applies configured defaults, and returns a `WebSocketStream` that respects `LeaveOpen`.
- When you need directional views, accept the `WebSocket` manually and use `_streams.CreateReadable(webSocket)` / `_streams.CreateWritable(webSocket)` to split inbound and outbound flows while still honoring global defaults.

### Minimal Host Wiring

For the simplest end-to-end setup, register the module via auto-registrars and expose a controller route:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddKoanAutoRegistrars();

var app = builder.Build();
app.MapControllers();
app.Run();
```

This bootstraps the `Koan.WebSockets` registrar, binds `WebSocketStreamOptions`, and makes `IWebSocketStreamFactory` available for any MVC controller that needs to accept upgrades.

## Operational Signals

- Provenance now lists `message-type`, `leave-open`, and `sub-protocol` for the `Koan.WebSockets` module, helping operators confirm runtime state.
- Validation runs at startup; misconfigured enums or boolean values fail fast during host boot.

## Related

- SSE guidance: `docs/proposals/koan-dotnet10-opportunity-map/how-to/sse/how-to-adopt-koan-sse-streaming.md`
- Web API controller shape: `docs/decisions/WEB-0035-entitycontroller-transformers.md`
