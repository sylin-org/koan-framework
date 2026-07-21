# B2 WebSocketStream Adapter Progress

**Contract**

- **Goal**: Deliver WebSocket stream adapters and integrations aligned with B2 scope.
- **Outputs**: Reusable adapter API, DI/registrar wiring, sample usage, docs.
- **Ready When**: All checklist items marked complete; module validated via tests/docs build.
- **Errored When**: Missing registrations, failing tests, or undocumented behaviors.

## Checklist

- [x] Confirm `.NET 10` `WebSocketStream` factory surface.
- [x] Scaffold `Koan.WebSockets` project with options + adapter helpers.
- [x] Provide `HttpContext.AcceptWebSocketStreamAsync()` extension for hosts.
- [x] Implement DI registration (`AddWebSocketStreamAdapters`) with sane defaults.
- [x] Add unit tests covering adapter behaviors and error handling.
- [ ] Introduce sample usage under `samples/` (controller + client walkthrough).
- [ ] Document adapter usage in `/docs/proposals/koan-dotnet10-opportunity-map/how-to/websockets/` and link from platform guides.
- [ ] Wire docs build + CI to include new module guidance.
