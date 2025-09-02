# ControlCommand (generic)

Contract
- Inputs: Verb (required), Target (optional), Arg (optional), Parameters (optional JSON bag)
- Output: N/A (command message)
- Errors: None intrinsic; handlers may reply with FlowAck/ControlResponse

Edge cases
- Missing Parameters or keys: accessors return false
- Wrong types: TryGet* helpers return false
- Large payloads: avoid; keep Parameters small and JSON-serializable

Examples

Create a command with parameters:

```
var cmd = new ControlCommand { Verb = "announce", Target = "bms:*" }
    .WithParam("immediate", true)
    .WithParam("filter", new { capability = "reading" });
```

Read parameters safely:

```
if (cmd.TryGetBoolean("immediate", out var imm) && imm) { /* do immediate */ }
if (cmd.TryGetObject<Filter>("filter", out var f)) { /* use filter */ }
```

Notes
- Prefer Parameters for verb-specific knobs; keep core contract generic
- Arg remains for simple one-off values; consider migrating to Parameters

Well-known verbs (Flow)
- announce — API auto-response (opt-out via `Sora:Flow:Control:AutoResponse:Announce:Enabled`) returns `ControlResponse<AdapterAnnouncement>` with the latest adapter identity (from the registry). Optional `Target` narrows the lookup to `system:adapter` or `system:adapter:instance`.
- ping — API auto-response (opt-out via `Sora:Flow:Control:AutoResponse:Ping:Enabled`) returns `FlowAck.Ok`. With a specific `Target`, presence is validated against the registry.

Target format
- `system:adapter` — matches all instances of the adapter
- `system:adapter:*` — explicit wildcard; treated like above
- `system:adapter:<instanceId>` — single instance (ULID) lookup
