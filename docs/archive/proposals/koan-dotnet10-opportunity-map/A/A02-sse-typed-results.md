# A2 — SSE Streaming Wrappers (Minimal + MVC)

**Intent**: Make streaming **Server-Sent Events** first‑class in Koan—typed and string modes—so agents, MCP and AI endpoints get reliable server→client push without WebSocket overhead.  
**Why**: ASP.NET Core 10 ships `TypedResults.ServerSentEvents(...)` and `SseItem<T>`. We add simple, unified APIs across Minimal **and** MVC. citeturn1search0turn1search6turn1search2

## Plan
**Touch modules**: new `Koan.Web.Sse` + small helpers in `Koan.Mcp` (uses SSE by default). fileciteturn0file15 fileciteturn0file13  
1) **Minimal API** helper:
   ```csharp
   public static class Sse
   {
       // For JSON-serializable items (uses ASP.NET Core typed SSE)
       public static IResult Stream<T>(IAsyncEnumerable<T> source, string? eventType = null)
           => TypedResults.ServerSentEvents(source, eventType ?? "message");

       // For pre-serialized strings; avoids JSON entirely
       public static IResult StreamRaw(IAsyncEnumerable<string> lines, string eventType = "message")
           => TypedResults.ServerSentEvents(lines, eventType);
   }
   ```
2) **MVC** helper (controller base class):
   ```csharp
   protected Task<IActionResult> SseRaw(IAsyncEnumerable<string> lines)
       => System.Net.ServerSentEvents.SseFormatter
           .WriteAsync(lines, Response.BodyWriter.AsStream(), (_, __) => {}, HttpContext.RequestAborted)
           .ContinueWith(_ => new EmptyResult() as IActionResult);
   ```
   Uses the **System.Net.ServerSentEvents** APIs for formatting/writing. citeturn1search4turn1search15
3) **Koan.Mcp**: swap ad‑hoc SSE writers for `Koan.Web.Sse` helpers. fileciteturn0file15

## Guardrails
- **Timeouts & heartbeats** configurable.  
- Back‑pressure: batch sources or use `Channel<T>` where needed.  
- Keep **WebSockets** available where bidirectional is required (see B02). citeturn0search3

## Acceptance Criteria
- Minimal sample streams `IAsyncEnumerable<T>` and `IAsyncEnumerable<string>` successfully.  
- MVC sample streams `IAsyncEnumerable<string>` with correct SSE framing.  
- MCP demo uses shared SSE helpers. fileciteturn0file13

## Tests
- K6 or HTTP REPL scripts assert stream shape + reconnection via `Last-Event-ID`.  
- Load test against RabbitMQ‑driven publisher (smoke). fileciteturn0file15
