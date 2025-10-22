# AI-0014 - MCP Code Mode: JavaScript Execution Surface

Status: Approved
Date: 2025-10-07
Owners: Koan AI Platform, Koan.Mcp Team

## Context

- LLM agents using traditional MCP tools face token bloat as entity count grows: each tool (collection, getById, upsert, delete, etc.) adds schema to context, and multi-step workflows require 5-10 roundtrips.
- Industry trend toward "code mode" (Cloudflare, Anthropic) shows 40-60% token reduction and improved reliability by letting LLMs write small programs instead of orchestrating individual tool calls.
- Koan's entity-first patterns map naturally to typed SDK: `Todo.collection()`, `todo.save()` become JavaScript methods with strong type hints.
- Current Koan.MCP exposes entity operations as discrete tools; we need an optional code execution path that preserves entity semantics while reducing context overhead.

## Decision

- Add **code mode** capability to `Koan.Mcp` core package enabling JavaScript execution against entity operations via synchronous SDK bindings.
- Expose **four operational modes** (Auto, Code, Tools, Full) allowing developers to optimize for token efficiency vs compatibility.
- Generate **TypeScript definitions** (`.d.ts`) from entity metadata to guide LLM code generation without requiring TypeScript execution.
- Use **Jint** (pure .NET ES2015+ engine) for sandboxed JavaScript runtime with CPU, memory, and recursion quotas.
- Bind **synchronous SDK** (`SDK.Entities.*`, `SDK.Out.*`) to JavaScript context; simplicity over async complexity reduces LLM context requirements.
- Register `koan.code.execute` as first-class MCP tool alongside traditional entity tools, controlled by exposure mode configuration.

## Operational Modes

Exposure modes control tool surface exposed to clients:

| Mode | Tools Exposed | SDK Available | Use Case |
|------|---------------|---------------|----------|
| **Auto** | Detects client capabilities; falls back to Full | Yes | Production default, adapts to client |
| **Code** | `koan.code.execute` only | Yes | Token-optimized for modern LLM agents |
| **Tools** | Entity tools only (no code execution) | No | Legacy MCP clients, compliance requirements |
| **Full** | Both code execution and entity tools | Yes | Development, migration, maximum compatibility |

Configuration hierarchy:
```
Entity [McpEntity(Exposure = "tools")] → Assembly [McpDefaults(Exposure = "code")] → appsettings.json → Auto
```

## Implementation Notes

### SDK Design (Synchronous Interface)

```javascript
// LLM generates JavaScript (not TypeScript - simpler context)
SDK.Entities.Todo.collection({ filter: { completed: false }, pageSize: 10 });
// Returns: { items: [...], page: 1, pageSize: 10, totalCount: 42 }

SDK.Entities.Todo.getById("todo-123", "tenant-abc");
// Returns: { id: "todo-123", title: "...", ... }

SDK.Entities.Todo.upsert({ id: "...", title: "Updated" }, "tenant-abc");
// Returns: { id: "...", title: "Updated", ... }

SDK.Entities.Todo.delete("todo-123");
// Returns: 1 (count deleted)

SDK.Out.answer("Found 5 incomplete todos");
// Sends response to user
```

**Why synchronous?**
- Smaller LLM context (no async/await syntax, Promise chains)
- Simpler error handling (try/catch vs Promise rejections)
- Easier for LLMs to generate correct code
- Jint executes C# async methods synchronously in engine context

### TypeScript SDK Generation

Generate `.d.ts` at startup from `McpEntityRegistry`:
```typescript
declare namespace Koan {
  namespace Entities {
    interface Todo { id: string; title: string; completed: boolean; }
    const Todo: {
      collection(params?: { filter?: any; pageSize?: number; set?: string }): { items: Todo[]; totalCount: number };
      getById(id: string, set?: string): Todo;
      upsert(model: Todo, set?: string): Todo;
      delete(id: string, set?: string): number;
      deleteMany(ids: string[], set?: string): number;
    };
  }
  namespace Out {
    function answer(text: string): void;
    function info(message: string): void;
    function warn(message: string): void;
  }
}
```

Cache generated SDK at startup; invalidate on hot reload via `IHostApplicationLifetime.ApplicationStarted` hook.

### Security Sandbox (Jint Configuration)

```csharp
new Engine(opts => {
    opts.TimeoutInterval(TimeSpan.FromMilliseconds(2000));  // CPU limit
    opts.LimitMemory(64_000_000);                           // 64 MB
    opts.LimitRecursion(100);                               // Stack depth
    opts.AllowClr(false);                                   // No reflection
});
```

### Dataset and Relationship Routing

Support Koan's multi-tenant patterns:
```javascript
// Dataset routing
SDK.Entities.Todo.collection({ set: "tenant-123" });

// Generate TypeScript enum from registered sets (via EntityContext discovery)
type KoanSet = "default" | "tenant-123" | "tenant-456";
```

Relationship expansion:
```javascript
SDK.Entities.Todo.getById("id", { with: "assignedUser,tags" });
```

Generate enum from relationship attributes:
```typescript
type TodoRelationships = "assignedUser" | "tags" | "all";
```

### Error Handling

JavaScript exceptions for failed operations (better LLM interpretation):
```javascript
try {
  const todo = SDK.Entities.Todo.getById("nonexistent");
} catch (err) {
  SDK.Out.warn(`Not found: ${err.message}`);
}
```

### Mutation Control

Respect `AllowMutations` attribute:
```csharp
[McpEntity(Name = "AuditLog", AllowMutations = false)]
public class AuditLog : Entity<AuditLog> { }
```

Generated SDK excludes mutation methods:
```typescript
const AuditLog: {
  collection(): { items: AuditLog[] };  // Read-only
  // No upsert, delete, deleteMany
}
```

### Integration with McpRpcHandler

Extend `tools/list` to include code execution tool when enabled:
```csharp
[JsonRpcMethod("tools/list")]
public Task<ToolsListResponse> ListToolsAsync(CancellationToken ct)
{
    var mode = ResolveExposureMode();
    var tools = new List<ToolDescriptor>();

    // Add code execution tool if enabled
    if (mode == McpExposureMode.Code || mode == McpExposureMode.Full)
    {
        var codeExecutor = _services.GetService<ICodeExecutor>();
        if (codeExecutor != null)
        {
            tools.Add(CreateCodeExecutionTool());
        }
    }

    // Add entity tools if enabled
    if (mode == McpExposureMode.Tools || mode == McpExposureMode.Full)
    {
        tools.AddRange(GetEntityTools());
    }

    return Task.FromResult(new ToolsListResponse { Tools = tools });
}
```

### Boot Report

```
[INFO] Koan.Mcp: Exposure mode: Auto (default)
[INFO] Koan.Mcp: Code execution: Enabled (Jint runtime)
[INFO] Koan.Mcp: SDK entities: 12 (Todo, User, Order, AuditLog, ...)
[INFO] Koan.Mcp: Sandbox limits: 2000ms CPU, 64MB memory, recursion depth 100
```

## Guidance for AI-Assisted Development

- SDK bindings execute synchronously from JavaScript perspective; Jint blocks on C# async methods internally.
- TypeScript definitions are **documentation only**; LLMs generate JavaScript, not TypeScript.
- Cache SDK `.d.ts` at startup using `AggregateBags` pattern; regenerate on hot reload signal.
- Test code mode against REST endpoints: same inputs should yield equivalent outputs (headers, warnings, dataset routing).
- When extending SDK domains (future: Memory, Files), keep each domain orthogonal and well-scoped.
- Entity operation proxy uses reflection to invoke `EndpointToolExecutor`; minimize per-call overhead.

## Consequences

- New dependency: `Jint` (~500 KB, pure .NET, BSD-2-Clause license) added to `Koan.Mcp`.
- Exposure modes allow gradual adoption: start with Auto/Full, optimize to Code once validated.
- LLM agents can execute multi-step workflows (5-10 operations) in single roundtrip vs 5-10 roundtrips with traditional tools.
- Token usage reduction: 40-60% for multi-step scenarios (measured via SDK schema size vs individual tool schemas).
- Security: Sandboxed execution prevents file system, network, reflection, and CLR access; configurable quotas enforce resource limits.
- Developer experience: Package reference to `Koan.Mcp` enables code mode automatically; zero additional configuration required for basic usage.
- Future extensibility: `ICodeExecutor` abstraction allows swapping Jint for ClearScript (V8) or other runtimes without API changes.
- Testing strategy: Vertical slice implementation (Todo entity end-to-end) validates approach before expanding to full entity surface.

## Migration Path

1. **Existing MCP clients**: No changes required; Auto mode defaults to Full for unknown clients.
2. **Opt-in code mode**: Set `Exposure = "code"` in appsettings or assembly attribute.
3. **Per-entity overrides**: Use `[McpEntity(Exposure = "tools")]` for compliance-sensitive entities.
4. **Telemetry-driven optimization**: Monitor code execution vs tool call ratios; switch to Code mode when code execution dominates.

## References

- Cloudflare Code Mode: https://blog.cloudflare.com/code-mode/
- Design document: `docs/external/designing_a_type_script_code_mode_surface_for_agentic_ai_mcp_alternative.md`
- Related ADR: AI-0012 (MCP JSON-RPC Runtime Standard)
## Delta Analysis & Implementation Plan (2025-10-07)

### Delta: Proposal vs. Implementation

- The current Koan.Mcp implementation exposes entities and tools as discrete operations, but does not yet provide a code-mode execution endpoint, TypeScript SDK generation, or sandboxed code execution as described in this ADR.
- Exposure modes (Auto, Code, Tools, Full), quota enforcement, and boot diagnostics for code mode are not yet implemented.
- To fully realize the agentic, code-enabled orchestration pattern, these features must be added.

### Implementation Plan

1. Add a code execution endpoint (`koan.code.execute`) using Jint for secure, synchronous JS execution.
2. Auto-generate a minimal TypeScript SDK surface from registered entities/tools, and expose it as `.d.ts` for agent prompt context.
3. Implement exposure modes and configuration hierarchy.
4. Enforce quotas, audit, and validation for all code runs.
5. Integrate with S16 (PantryPal) as the first proof of concept, registering all entities and workflows for code-mode orchestration.
6. Document the code-mode surface, SDK types, and usage patterns in both Koan.Mcp and S16 docs.
7. Provide developer samples and golden scenarios for testing and validation.

### Proof of Concept: S16 PantryPal

- S16 PantryPal will serve as the reference implementation and validation suite for MCP code mode.
- It will demonstrate agentic AI pipelines that reason over, discover, and orchestrate workflows using the code-enabled MCP service.
- Multi-step workflows (e.g., photo → detection → pantry update → meal suggestion) will be implemented as single code-mode scripts, showcasing the value of the approach.

This approach will ensure Koan.Mcp leads in agentic, code-enabled orchestration for modern AI systems, with S16 as a showcase for real-world value.

## Current Implementation Delta (2025-10-08)

The initial code-mode foundation has been implemented (Jint executor, synchronous SDK bindings, JSON unification ADR ARCH-0061). The following gaps remain to fully satisfy this ADR for S16 showcase readiness:

### Gaps

| Area | Gap | Impact | Priority |
|------|-----|--------|----------|
| Exposure Modes | No enum / hierarchy (Auto/Code/Tools/Full) resolution implemented | Cannot tailor tool surface per client | P0 |
| tools/list Filtering | Code tool & entity tools always (or implicitly) exposed | Over-tokenization in Code mode | P0 |
| TypeScript SDK | Uses System.Text.Json Nodes & lacks relationship/set enums; mutation filtering via AllowMutations not enforced in `.d.ts` | DX inconsistency & missing guidance | P1 |
| Set & Relationship Metadata | Not generating enumerations (e.g., `type TodoRelationships = ...`) | LLM cannot reliably enumerate expansions | P2 |
| Mutation Gating (SDK surface) | Runtime check throws, but TS generation still lists upsert/delete for read-only entity | Misleading types | P1 |
| SDK Regeneration | Only at startup; hot reload / change detection not wired | Stale definitions during iterative dev | P3 |
| Quota Tests | No tests for MaxSdkCalls, RequireAnswer, log truncation | Risk of silent regression | P1 |
| Error Code Coverage | Some codes implemented but not documented in output / tests | Incomplete contract clarity | P2 |
| Boot Report | No structured summary lines for exposure mode & quotas | Operator visibility reduced | P2 |
| Syntax Validation Surface | Internal method only, no tool / endpoint exposure | Harder preflight for agents | P3 |
| Security Hardening | No length cap on individual log messages / stack trace truncation | Potential prompt pollution | P3 |
| JSON Schema Source | TypeScript generator still using STJ JsonObject (post unification) | Divergent JSON model risk | P1 |

### Completed Elements
- Jint sandbox with CPU/memory/recursion limits.
- Synchronous entity operation proxies (collection, paging, getById, upsert, delete, deleteMany).
- Answer + log capture domain.
- Unified JSON representation via Newtonsoft (ARCH-0061).
- Negative-path tests (invalid tool, missing getById).
- Union type generation for entity sets & relationships (e.g. `type TodoSet = "default" | ...`, `type TodoRelationship = "assignedUser" | "tags" | "all"`) with test coverage (`UnionTypesSpec`).

## Implementation Delta Plan

### Phase 1 (Foundational Gaps - P0/P1)
1. Exposure Mode Enum & Resolution
  - Add `McpExposureMode { Auto, Code, Tools, Full }`.
  - Resolution order: Entity attribute → Assembly attribute → Config (`Koan:Mcp:ExposureMode`) → Auto.
  - Auto strategy: If client declares code capability (future header/cap), choose Full; else Full (fallback) until capability detection implemented.
  - Acceptance: `tools/list` returns only `koan.code.execute` in Code mode; only entity tools in Tools mode.

2. tools/list Filtering Logic
  - Modify `McpRpcHandler` or equivalent listing aggregator.
  - Unit tests: verify tool counts per mode.

3. TypeScript Generator Unification & Mutation Filtering
  - Switch generator to use `IJsonFacade` / `JToken` not `JsonObject`.
  - Omit upsert/delete/deleteMany signatures when `AllowMutations=false`.
  - Add test snapshot for a read-only entity.

4. Relationship & Set Metadata Stubs
  - Extend entity registration to expose: `AvailableSets`, `Relationships`.
  - Generate union types: `type TodoSet = "default" | ...` and `type TodoRelationship = "user" | "tags" | "all"`.
  - If absent, skip gracefully.

5. Quota & RequireAnswer Tests
  - Add tests: exceed sdkCalls, missing answer when RequireAnswer=true (configure via options injection), log truncation.

6. JSON Schema Source Update
  - Replace STJ schema extraction with JToken navigation; ensure no mixed DOM types remain in generator.

### Phase 2 (DX & Observability - P2)
7. Boot Report Enhancements
  - Add structured lines: `CodeMode: mode=Full enabled=true sdkCallsMax=0 logsMax=0 requireAnswer=false`.
  - Test: parse boot log for marker.

8. Error Code Documentation
  - Centralize error code constants; update ADR table mapping code→meaning.

9. Syntax Validation Tool (Optional)
  - Expose `koan.code.validate` (returns { valid: bool, error?: string }).

### Phase 3 (Polish & Hardening - P3)
10. Hot Reload Support for `.d.ts`
   - Hook into entity registry change or file watcher; regenerate on change.
11. Log & Stack Truncation Policy
   - Enforce max characters per log entry & stack snippet.
12. Future Engine Abstraction
   - Add guard to ensure `Runtime == "Jint"` else NotSupported result.

## Risk Mitigation Notes
- Introduce comprehensive integration test covering mode transitions (switch config and restart test host).
- Add analyzer or CI script to flag new `System.Text.Json.Nodes` usage under CodeMode namespace.

## Acceptance Criteria Summary
| Feature | Criteria |
|---------|----------|
| Exposure Modes | `tools/list` returns expected surface per mode; config switch reflected without code changes. |
| Mutation Filtering | Read-only entity `.d.ts` lacks mutation signatures; runtime still guards. |
| Quotas | Exceeding MaxSdkCalls returns `sdk_calls_exceeded`; missing answer returns `missing_answer`. |
| Relationship/Set Types | Present when metadata exists; absent otherwise without errors. |
| Boot Report | Contains standardized one-line summary with mode & quotas. |
| JSON Unification | No `System.Text.Json.Nodes` usages in generator. |

---
_Document extended on 2025-10-08 to reflect implementation delta and phased closure plan._

### 2025-10-08 Update: Exposure Mode Fallback Implementation

Initial implementation of exposure mode resolution shipped with infrastructure already present (`McpExposureMode` enum, `McpDefaultsAttribute`, configuration property). A logic gap caused `Auto` mode to return `Auto` instead of a concrete tool surface, risking empty `tools/list` responses for some clients.

Temporary mitigation now treats `Auto` as `Full` (code + entity tools) until handshake-based client capability detection is implemented (planned in Phase 2+). The resolution chain is:

1. `McpServerOptions.Exposure` (if explicit and not `Auto`)
2. First assembly-level `[assembly: McpDefaults(Exposure = ...)]`
3. Fallback => `Auto` → currently mapped to `Full` at runtime

Test Coverage: `ExposureModeSpec.AutoExposure_ShouldListCodeAndEntityTools` asserts that the fallback includes both `koan.code.execute` and at least one entity tool.

Planned Future Enhancement: Replace unconditional Full mapping with handshake-driven negotiation (e.g. client initialization metadata advertising code execution capability). Once implemented, `Auto` will pick `Code` for capable clients and `Tools` for legacy clients, or `Full` only when both are strongly beneficial (e.g. development sessions).

### 2025-10-08 Update: Phase 1 Test Coverage Additions

Added integration and unit-style tests to close foundational enforcement gaps:

| Test | Purpose | File |
|------|---------|------|
| `ExposureModeSpec.AutoExposure_ShouldListCodeAndEntityTools` | Verifies temporary Auto→Full fallback tool surface | `ExposureModeSpec.cs` |
| `QuotaSpec.MaxSdkCalls_ShouldEnforce` | Asserts quota error `sdk_calls_exceeded` (skips gracefully if unlimited) | `QuotaSpec.cs` |
| `QuotaSpec.RequireAnswer_ShouldEnforce` | Asserts `missing_answer` when answer omitted (conditional if flag on) | `QuotaSpec.cs` |
| `TypeScriptGenerationSpec.ReadOnlyEntity_ShouldOmitMutations` | Ensures read-only entity drops mutation signatures | `TypeScriptGenerationSpec.cs` |
| `QuotaSpec.Success_ShouldIncludeDiagnostics` | Smoke-validates successful execution path returns answer text | `QuotaSpec.cs` |

These tests elevate confidence in quota enforcement, exposure behavior, and TypeScript surface correctness ahead of Phase 2 (boot reporting, error code documentation). Future enhancement: add a harness to force specific CodeModeOptions per test to remove conditional assertions.

## Acceptance Gates

### Phase 1 (Implemented)
| Gate | Criteria | Evidence |
|------|----------|----------|
| Exposure Fallback | `Auto` maps to Full (temporary) and lists code + ≥1 entity tool | `ExposureModeSpec.AutoExposure_ShouldListCodeAndEntityTools` |
| Mutation Omission | Read-only entity lacks upsert/delete/deleteMany signatures in `.d.ts` | `TypeScriptGenerationSpec.ReadOnlyEntity_ShouldOmitMutations` |
| Quota Enforcement | Exceeding SDK calls surfaces `sdk_calls_exceeded` (when limit configured) | `QuotaSpec.MaxSdkCalls_ShouldEnforce` |
| Require Answer | Missing answer surfaces `missing_answer` (when `RequireAnswer=true`) | `QuotaSpec.RequireAnswer_ShouldEnforce` |
| Success Path | Normal script returns answer text (no error codes) | `QuotaSpec.Success_ShouldIncludeDiagnostics` |
| JSON Unification | No `System.Text.Json.Nodes` under CodeMode SDK generator | Code review & grep (migration complete) |

### Phase 2 (Planned – Must Pass Before Public Preview)
| Gate | Planned Criteria |
|------|------------------|
| Boot Report Line | Single structured line: `CodeMode: enabled=true mode=Full maxSdkCalls=0 requireAnswer=false runtime=Jint` present in startup logs |
| Error Code Registry | Central static class enumerates codes; executor only references constants; ADR table authoritative |
| Error Code Table Test | Reflection test ensures every constant appears in ADR (or doc check harness) |
| Syntax Validation Tool (Optional) | `koan.code.validate` tool listed in Full/Code mode with schema `{ code: string }` and returns structured `{ valid: bool, error?: string }` |
| Deterministic Quota Tests | Test fixture variant with `MaxSdkCalls>0` & `RequireAnswer=true` ensures non-conditional assertions |
| Relationship/Set Unions Populated (Stretch) | At least one entity demonstrates non-empty `AvailableSets` & `RelationshipNames` generating union types |

Implemented Additions (2025-10-08):
- Deterministic Quota Fixture: `StrictQuotaTestPipelineFixture` sets `MaxSdkCalls=2`, `RequireAnswer=true`.
- Tests: `QuotaStrictSpec.Quota_ShouldEnforceDeterministically` (sdk_calls_exceeded), `QuotaStrictSpec.RequireAnswer_ShouldAlwaysEnforce` (missing_answer), and success path.


#### Phase 2 Error Code Registry (Centralized)

Centralized in `CodeModeErrorCodes` (namespace `Koan.Mcp.CodeMode.Execution`). All surfaced error codes MUST originate from these constants.

| Constant | Code | When Emitted | Notes |
|----------|------|--------------|-------|
| `InvalidCode` | `invalid_code` | Submitted source is null/empty/whitespace | Pre-execution validation failure |
| `CodeTooLong` | `code_too_long` | Source length > `SandboxOptions.MaxCodeLength` | Prevents excessive parsing cost |
| `SdkCallsExceeded` | `sdk_calls_exceeded` | Post-run: total entity calls > `CodeModeOptions.MaxSdkCalls` | Enforced after execution (no partial runs) |
| `MissingAnswer` | `missing_answer` | `RequireAnswer=true` but `SDK.Out.answer()` not called | Encourages explicit model outputs |
| `JavaScriptError` | `javascript_error` | Jint surfaced runtime error (with location) | Includes line/column when available |
| `Timeout` | `timeout` | Execution exceeded `SandboxOptions.CpuMilliseconds` | Raised via Jint timeout exception |
| `ExecutionError` | `execution_error` | Unexpected executor or tool exception catch-all | Generic fallback, prefer specific codes first |
| `ToolNotFound` | `tool_not_found` | Requested tool name missing from registry | Endpoint discovery / client bug |
| `ServiceUnavailable` | `service_unavailable` | Backend `IEntityEndpointService` not resolved from DI scope | Misconfiguration / partial registration |
| `InvalidPayload` | `invalid_payload` | JSON payload translation failure / schema mismatch | Thrown during request translation phase |

Planned Additional (if implemented):

| Candidate | Purpose | Trigger |
|-----------|---------|---------|
| `syntax_error` | Distinguish parse errors from runtime errors if validate tool surfaces them separately | Future `koan.code.validate` implementation |
| `capability_unsupported` | Auto negotiation declines unsupported mode | Capability handshake (Phase 3) |

Consistency Gates:
1. No raw string literals of these codes outside `CodeModeErrorCodes` (except ADR documentation).
2. Test harness will reflect constants and assert presence in this table (Phase 2 gate).
3. Any new constant requires ADR table update in same commit.

#### Implemented: Syntax Validation Tool (`koan.code.validate`)

A lightweight validation tool reduces failed execution cycles by allowing agents to preflight syntax before quota-impacting runs.

Contract Block:
- Input: `{ code: string }`
- Output (success): `{ valid: true }`
- Output (failure): `{ valid: false, error: string }`
- Error Modes: none (always succeeds with structured response)

Edge Cases:
- Empty code -> `valid=false` with `error="Code cannot be empty"`
- Excess length > `MaxCodeLength` -> `valid=false` with length message (mirrors `code_too_long` semantics without emitting execution error code)
- Unterminated block/comment -> first parse diagnostic surfaced
- Non-UTF8 (should not reach tool; assume hosting layer normalized)

Acceptance Gate (implemented):
1. Tool appears only when exposure mode includes Code tool surface (Code / Full / Auto→Full fallback).
2. Validation does not increment SDK call metrics (no executor run path, direct parse only).
3. Invalid code never produces `CodeModeErrorCodes` values—these remain reserved for execution.
4. Tests: `ValidationToolSpec.List_ShouldContainValidationTool`, `ValidationToolSpec.Validate_ValidScript`, `ValidationToolSpec.Validate_InvalidScript`, `ValidationToolSpec.Validate_Empty`.
5. Result contract always `Success=true` with `{ valid: bool, error? }` – never an execution error envelope.

### Phase 3 (Future Hardening)
| Gate | Future Criteria |
|------|-----------------|
| Log Truncation Policy | Max log length & stack trace truncation enforced; test verifying truncation marker |
| Hot Reload SDK | Change to entity registration triggers regeneration (file timestamp or memory snapshot diff) |
| Capability Negotiation | `Auto` selects Code or Tools based on client handshake metadata (test simulating capabilities) |

Failure to meet any Phase 2 gate blocks promotion to public preview; Phase 3 gates required for GA.
