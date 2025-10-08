# S16 MCP Code Mode Sample

This sample demonstrates Koan.Mcp's **Code Mode** capabilities, which allow LLM agents to write JavaScript programs that execute multiple entity operations in a single roundtrip, dramatically reducing token usage and latency.

## Overview

Traditional MCP uses individual tool calls for each operation. Code Mode provides a TypeScript-documented JavaScript SDK that lets agents write programs like:

```javascript
// Traditional MCP: 3 separate tool calls
// 1. Todo.collection()
// 2. Todo.upsert(newTodo)
// 3. Todo.collection() again

// Code Mode: 1 execution with multiple operations
const todos = SDK.Entities.Todo.collection({ filter: { isCompleted: false } });
const highPriority = todos.items.filter(t => t.priority === "high");

if (highPriority.length > 5) {
  SDK.Out.warn(`You have ${highPriority.length} high-priority tasks!`);
}

const newTodo = SDK.Entities.Todo.upsert({
  title: "Review high-priority tasks",
  priority: "urgent",
  isCompleted: false
});

SDK.Out.answer(`Created task: ${newTodo.title}. You have ${todos.totalCount} total tasks.`);
```

## Key Features

- **40-60% Token Reduction**: Multi-step workflows execute in one roundtrip
- **Synchronous Interface**: Simple API (no async/await complexity for LLMs)
- **Full CRUD**: collection, getById, upsert, delete, deleteMany
- **Dataset Routing**: Multi-tenant support via `set` parameter
- **Relationship Expansion**: Eager loading via `with` parameter
- **Sandbox Security**: CPU time, memory, and recursion limits
- **TypeScript Definitions**: Type-safe SDK documentation for LLMs at `/mcp/sdk/definitions`

## Configuration

### Exposure Modes

The `Exposure` setting controls what's exposed to MCP clients:

- **`auto`** (default): Detect client capabilities, fallback to full
- **`code`**: Code execution only (token optimized)
- **`tools`**: Traditional entity tools only (legacy)
- **`full`**: Both code and tools (maximum compatibility)

### Sandbox Settings

```json
{
  "Koan": {
    "Mcp": {
      "CodeMode": {
        "Enabled": true,
        "Runtime": "Jint",
        "Sandbox": {
          "CpuMilliseconds": 2000,
          "MemoryMegabytes": 64,
          "MaxRecursionDepth": 100
        }
      }
    }
  }
}
```

## Running

From the repository root:

```powershell
pwsh ./scripts/cli-run.ps1 S16.McpCodeMode
```

Or with .NET CLI:

```bash
dotnet run --project samples/S16.McpCodeMode
```

## Endpoints

- **STDIO Transport**: `koan.code.execute` tool available via stdin/stdout
- **HTTP+SSE Transport**: `POST /mcp/rpc` with `tools/call` method
- **SDK Definitions**: `GET /mcp/sdk/definitions` - TypeScript SDK for LLM guidance
- **Capabilities**: `GET /mcp/capabilities` - Server capability discovery

## Example Usage

### TypeScript SDK Definitions

```bash
curl http://localhost:5000/mcp/sdk/definitions
```

Returns TypeScript definitions:

```typescript
declare namespace Koan {
  namespace Entities {
    interface Todo {
      id: string;
      title: string;
      description?: string;
      isCompleted: boolean;
      priority: string;
      createdAt: string;
      completedAt?: string;
    }

    interface ITodoOperations {
      collection(params?: {
        filter?: any;
        pageSize?: number;
        set?: string;
        with?: string
      }): {
        items: Todo[];
        page: number;
        pageSize: number;
        totalCount: number
      };

      getById(id: string, options?: {
        set?: string;
        with?: string
      }): Todo;

      upsert(model: Todo, options?: {
        set?: string
      }): Todo;

      delete(id: string, options?: {
        set?: string
      }): number;

      deleteMany(ids: string[], options?: {
        set?: string
      }): number;
    }

    const Todo: ITodoOperations;
  }

  namespace Out {
    function answer(text: string): void;
    function info(message: string): void;
    function warn(message: string): void;
  }
}
```

### Code Execution via HTTP

```bash
curl -X POST http://localhost:5000/mcp/rpc \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "method": "tools/call",
    "params": {
      "name": "koan.code.execute",
      "arguments": {
        "code": "const todos = SDK.Entities.Todo.collection(); SDK.Out.answer(\`Found ${todos.totalCount} todos\`);"
      }
    },
    "id": 1
  }'
```

Response:

```json
{
  "jsonrpc": "2.0",
  "result": {
    "success": true,
    "result": {
      "output": "Found 5 todos",
      "metrics": {
        "executionMs": 45,
        "memoryMb": 2.3,
        "entityCalls": 1
      }
    }
  },
  "id": 1
}
```

## Token Savings Example

### Traditional MCP (3 roundtrips)

```
User: "Create a task and tell me my stats"

Agent → tools/call Todo.collection
Server → { items: [...], totalCount: 10 }

Agent → tools/call Todo.upsert { title: "New task" }
Server → { id: "123", title: "New task" }

Agent → tools/call Todo.collection { filter: { isCompleted: false } }
Server → { items: [...], totalCount: 5 }

Agent → User: "Created 'New task'. You have 10 total tasks, 5 incomplete."
```

Total: ~2,400 tokens (estimated)

### Code Mode (1 roundtrip)

```
User: "Create a task and tell me my stats"

Agent → tools/call koan.code.execute
{
  "code": `
    const all = SDK.Entities.Todo.collection();
    const incomplete = all.items.filter(t => !t.isCompleted);

    const newTodo = SDK.Entities.Todo.upsert({
      title: "New task",
      isCompleted: false
    });

    SDK.Out.answer(
      \`Created '\${newTodo.title}'. You have \${all.totalCount} total tasks, \${incomplete.length} incomplete.\`
    );
  `
}

Server → {
  success: true,
  result: {
    output: "Created 'New task'. You have 10 total tasks, 5 incomplete."
  }
}

Agent → User: [forwards output]
```

Total: ~1,100 tokens (estimated)

**Savings: ~54% reduction** in this workflow

## Architecture Decision

See [AI-0014: MCP Code Mode](../../docs/decisions/AI-0014-mcp-code-mode.md) for the full technical design and rationale.

## Related Samples

- **S12.MedTrials.McpService**: Traditional MCP entity tools
- **S13.DocMind**: Vector search + MCP integration
- **S14.AdapterBench**: Multi-provider data adapter benchmarking
