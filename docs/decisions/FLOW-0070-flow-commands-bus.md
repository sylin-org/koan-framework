# FLOW-0070: Flow Command Bus (Fire-and-Forget, Targetable, Model-Free)

**Status:** Proposed 2025-09-03

## Context

Operational actions (seed, purge, recalc, etc.) in Koan Flow have historically required bespoke HTTP endpoints and orchestration logic, leading to boilerplate, model coupling, and poor discoverability. There is a need for a unified, minimal, and idiomatic command facility that:
- Is not coupled to Flow models or event store
- Is fire-and-forget (no reply, no await)
- Allows broadcast or targeted dispatch (adapter/system)
- Is discoverable and terse for both senders and handlers
- Supports a generic HTTP endpoint for UI/CLI/automation

## Decision

- Introduce a top-level Flow Command Bus abstraction:
  - `Flow.Outbound.SendCommand(name, args, target)` for dispatch
  - `Flow.Inbound.On(name, handler, target)` for handler registration
- Commands are not Flow models and are not persisted as events/readings
- All dispatch is fire-and-forget; no reply or orchestration
- Targeting: `target` may be null (broadcast) or a system/adapter id
- Generic HTTP endpoint: `POST /api/flow/commands/{command}?k=v&target=xyz` (202 Accepted)
- Args are flattened from query string and/or JSON body, with simple type coercion
- Handlers are registered per adapter (optionally with target)
- Unknown commands or missing handlers are logged, not errored

## Consequences

- Dramatically reduces boilerplate for operational actions
- Enables UI/CLI/automation to trigger any command with a single endpoint
- Keeps command logic out of Flow event store and models
- Extensible for future ops (purge, recalc, etc.)
- All command handling is parallel and non-blocking by default

## Example Usage

**Send a command:**
```csharp
Flow.Outbound.SendCommand("seed", new { count = 5 }, target: "bms");
```

**Register a handler:**
```csharp
Flow.Inbound.On("seed", (ctx, args, ct) => {
    var count = args.TryGetValue("count", out var v) ? Convert.ToInt32(v) : 1;
    Generate(count);
}, target: "bms");
```

**HTTP:**
```
POST /api/flow/commands/seed?count=5&target=bms
```

## Migration

- Implement Flow.Outbound/Inbound APIs and registry
- Add generic HTTP endpoint
- Register handlers in adapters
- Deprecate old orchestration endpoints

## See Also
- [ARCH-0042](./ARCH-0042-module-docs-and-readme.md)
- [FLOW-0060](./FLOW-0060-flow-event-bus.md)
