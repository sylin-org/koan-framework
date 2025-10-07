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
