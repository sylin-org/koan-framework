# B2 — WebSocketStream Adapters

**Intent**: Provide a `Stream`-based wrapper around WebSockets using **System.Net.WebSockets.WebSocketStream** to unify stream processing code paths and enable true bidirectional channels when SSE is not enough. citeturn0search3

## Plan
1) New `Koan.WebSockets` module exposing helpers: `AsStream(ClientWebSocket ws)` returns `WebSocketStream` for pipelines. citeturn0search6
2) Guidance when to choose SSE vs WebSockets; update MCP/agents samples accordingly.

## Acceptance Criteria
- Sample performs duplex chat over `WebSocketStream` with back‑pressure tests.
