---
type: GUIDE
domain: mcp
title: "MCP over HTTP How-To"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-06-20
framework_version: v0.17
validation:
  date_last_tested: 2026-06-20
  status: verified
  scope: all-examples-tested
  notes: "Now covers Streamable HTTP (AI-0037) as the modern default HTTP transport (single /mcp endpoint, spec 2025-06-18); the legacy HTTP+SSE /sse+/rpc transport documented in detail here is deprecated and opt-in (EnableLegacySseTransport). Section 5 'Authentication' updated for the SEC-0006 embedded AS / Koan.bearer resource-server edge; deep AS flow in oauth-server-howto.md. [McpIgnore] field exclusion covered by tests/Suites/Samples/Koan.Mcp.FieldExclusion.Tests."
related_guides:
  - entity-capabilities-howto.md
  - patch-capabilities-howto.md
  - canon-capabilities-howto.md
---

# MCP over HTTP How-To

**Related Guides**
- [Entity Capabilities](entity-capabilities-howto.md) - Entity-first patterns for MCP tool definitions
- [Patch Capabilities](patch-capabilities-howto.md) - Partial update patterns via MCP tools
- [Canon Capabilities](canon-capabilities-howto.md) - Multi-source aggregation exposed via MCP

---

Think of this guide as a conversation with a colleague who's integrated MCP servers into cloud IDEs, AI agent platforms, and collaborative coding environments. We'll explore Koan's HTTP + Server-Sent Events (SSE) transport—how to expose entity operations as MCP tools, handle authentication, and stream real-time responses to remote clients.

MCP over HTTP lets AI agents and IDEs call your backend operations as if they were local functions—with type safety, streaming responses, and zero client-side infrastructure.

## Transport at a glance

Koan ships **two** HTTP transports for MCP, and the modern one is now the default.

**Streamable HTTP** is the modern default (MCP spec [2025-06-18](https://modelcontextprotocol.io)). It is a **single** endpoint at `{baseRoute}` (default `/mcp`) that multiplexes everything over `POST` / `GET` / `DELETE` — see [AI-0037: MCP Streamable HTTP Transport](../decisions/AI-0037-mcp-streamable-http-transport.md). It is what mounts when you turn the HTTP transport on (`Koan:Mcp:EnableHttpSseTransport: true`), with no extra flag.

A 60-second tour with `curl`:

```bash
# 1. Initialize — POST /mcp. Read the session id out of the RESPONSE header.
curl -i -X POST http://localhost:5110/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
# → 200, header:  Mcp-Session-Id: 5f3c...   (the response is one text/event-stream SSE answer)

# 2. List/call tools — POST /mcp again, carrying that session id.
SESSION="5f3c..."
curl -X POST http://localhost:5110/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: $SESSION" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'

# 3. Open the standalone server-push stream — GET /mcp (Accept: text/event-stream).
curl -N http://localhost:5110/mcp \
  -H "Accept: text/event-stream" \
  -H "Mcp-Session-Id: $SESSION"

# 4. Terminate the session — DELETE /mcp.
curl -X DELETE http://localhost:5110/mcp -H "Mcp-Session-Id: $SESSION"
```

A `POST` carrying a request is answered as a per-request `text/event-stream` SSE response (or a single `application/json` body); a `POST` carrying only a notification returns `202`. The `GET` stream is resumable via `Last-Event-ID`, and only one such stream may exist per session (a second returns `409`). A `404` with JSON-RPC error code `-32001` on a request is the **re-initialize** signal (the session has expired — `initialize` again).

> **The legacy HTTP+SSE transport is deprecated.** The detailed mechanics in the rest of this guide — the two-endpoint `{baseRoute}/sse` + `{baseRoute}/rpc` shape (MCP spec 2024-11-05) — describe that **legacy** transport. It still works, byte-for-byte, but it is **opt-in only**: set `"EnableLegacySseTransport": true` (it does not mount otherwise, and `/sse` + `/rpc` would `404`). New deployments should prefer Streamable HTTP above.

## Contract

**What this guide provides:**
- How to expose Koan entities as MCP tools over HTTP
- When to use HTTP+SSE vs STDIO transports
- Authentication, CORS, and security patterns
- Session management and connection lifecycle
- Real-time streaming via Server-Sent Events
- Tool discovery and capability endpoints

**Inputs:**
- A Koan web application (`builder.Services.AddKoan()` — referencing `Koan.Mcp` + `Koan.Web` is the whole opt-in)
- Entities decorated with `[McpEntity]` attributes
- The HTTP transport enabled in configuration (`EnableHttpSseTransport: true`) — this mounts **Streamable HTTP** (the default); the **legacy** `/sse`+`/rpc` pair is opt-in via `EnableLegacySseTransport: true`
- Optional: authentication provider (OAuth, API keys)

**Outputs:**
- **Streamable HTTP (default):** a single `/mcp` endpoint serving `POST` (JSON-RPC requests, answered as per-request SSE or `application/json`), `GET` (the standalone server-push SSE stream), and `DELETE` (session terminate)
- **Legacy HTTP+SSE (opt-in, deprecated):** `/mcp/sse` streaming JSON-RPC responses + `/mcp/rpc` accepting tool invocations
- `/mcp/capabilities` endpoint exposing tool metadata
- Real-time acknowledgments, results, errors, and heartbeats

**Error modes:**
- Session not found → 404
- Invalid JSON-RPC → 400 with error event
- Authentication failure → 401
- Rate limit exceeded → 429
- Tool execution failure → JSON-RPC error response

**Success criteria:**
- SSE stream establishes with session ID
- `tools/list` returns entity operations
- `tools/call` executes and streams results
- Heartbeats maintain connection
- Capabilities endpoint documents transport

**See also:**
- MCP specification: [Model Context Protocol](https://modelcontextprotocol.io)
- Transport architecture: [MCP-0001: HTTP+SSE Transport](../archive/proposals/koan-mcp-http-sse-transport.md)

---

## 0. Prerequisites and When to Use

### When to Use HTTP+SSE

**Use HTTP+SSE when:**
- AI agents run remotely (cloud IDEs, hosted assistants)
- You need web-standard authentication (OAuth, API keys)
- Client is browser-based or behind corporate firewall
- You want stateless horizontal scaling
- Multiple clients connect to same server
- Need CORS support for browser clients

**Example scenarios:**
- **Cloud IDE integration:** GitHub Copilot, Cursor, Windsurf connecting to your backend
- **Hosted AI agents:** Claude Code Mode, custom GPTs calling your API
- **Web dashboards:** Admin UIs invoking backend operations via MCP
- **Multi-tenant SaaS:** Different customers' agents connecting with OAuth tokens

**Use STDIO instead when:**
- AI agent runs locally (same machine as server)
- You control process lifecycle (parent spawns MCP server)
- Need simplest possible setup (no network config)
- Single client per server instance

### Decision Tree

```
Start: "I need to expose Koan entities to AI agents"
│
├─ Is the AI agent remote (different machine/cloud)?
│  ├─ Yes → Use HTTP+SSE (this guide)
│  └─ No ↓
│
├─ Do you need browser-based clients?
│  ├─ Yes → Use HTTP+SSE (CORS support)
│  └─ No ↓
│
├─ Do you need OAuth/API key authentication?
│  ├─ Yes → Use HTTP+SSE
│  └─ No ↓
│
├─ Do you need horizontal scaling (multiple server instances)?
│  ├─ Yes → Use HTTP+SSE (stateless sessions)
│  └─ No ↓
│
└─ Is the agent a local process you control?
   └─ Yes → Use STDIO (simpler setup)
```

### Prerequisites

Before following this guide:

1. **.NET 9 SDK** or later installed
   ```bash
   dotnet --version
   # Should be 9.0.0 or higher
   ```

2. **Koan packages:**
   ```xml
   <PackageReference Include="Koan.Core" Version="0.17.0" />
   <PackageReference Include="Koan.Web" Version="0.17.0" />
   <PackageReference Include="Koan.Mcp" Version="0.17.0" />
   ```

3. **Testing tools (optional):**
   ```bash
   # Install curl and jq for command-line testing
   curl --version
   jq --version
   ```

4. **Familiarity with:**
   - Entity<T> pattern (see [entity-capabilities-howto.md](entity-capabilities-howto.md))
   - JSON-RPC 2.0 specification
   - Server-Sent Events (SSE) basics

---

## 1. Quick Start

> **Deprecated.** From here on, this guide documents the **legacy** HTTP+SSE transport — the two-endpoint `{baseRoute}/sse` + `{baseRoute}/rpc` shape (MCP spec 2024-11-05). It is byte-faithful and still fully supported, but it is a thin shim over the same unified session/dispatch core as Streamable HTTP and has been **superseded**. It is **opt-in only** via `"EnableLegacySseTransport": true`. For new work, prefer Streamable HTTP — see [Transport at a glance](#transport-at-a-glance) and [AI-0037](../decisions/AI-0037-mcp-streamable-http-transport.md). The auth, CORS, nginx, session, and troubleshooting material below applies to the legacy transport.

**Scenario:** Expose a Todo entity to remote AI agents so they can create, read, update, and delete tasks.

### Step 1: Create the project

```bash
mkdir koan-mcp-http && cd koan-mcp-http
dotnet new web
dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Mcp
```

### Step 2: Add an MCP entity

```csharp
// Models/Todo.cs
using Koan.Mcp;
using Koan.Data.Core.Model;

[McpEntity(Description = "Simple task management", AllowMutations = true)]
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public int Priority { get; set; } = 3;
}
```

### Step 3: Configure the legacy HTTP+SSE transport

```json
// appsettings.Development.json
{
  "Koan": {
    "Mcp": {
      "EnableLegacySseTransport": true,
      "RequireAuthentication": false,
      "EnableCors": true,
      "AllowedOrigins": ["http://localhost:3000"],
      "PublishCapabilityEndpoint": true
    }
  }
}
```

### Step 4: Wire up Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// Reference = Intent: referencing Koan.Mcp (+ Koan.Web) is the whole opt-in. The Koan.Mcp auto-registrar
// runs AddKoanMcp() for you, and the MCP endpoint contributor maps /mcp/sse + /mcp/rpc INSIDE Koan's
// pipeline — so there is NO AddKoanMcp() / AddKoanWeb() / MapKoanMcpEndpoints() to call. AppHost, routing,
// auth middleware, and controller mapping are wired by the framework too.
builder.Services.AddKoan();

var app = builder.Build();
app.Run();
```

> The legacy HTTP+SSE transport is **off by default** — opt in with the config above (`Koan:Mcp:EnableLegacySseTransport: true`) for the `/sse` + `/rpc` pair. (`Koan:Mcp:EnableHttpSseTransport: true` is the master "HTTP transport on" switch, but on its own it mounts **Streamable HTTP** at `/mcp` instead — see [Transport at a glance](#transport-at-a-glance).) The endpoints self-gate on these flags, so they appear only once you opt in.

### Step 5: Run and test

```bash
# Terminal 1: Start server
dotnet run
# Server starts on http://localhost:5110

# Terminal 2: Open SSE stream
curl -N http://localhost:5110/mcp/sse
# event: connected
# data: {"sessionId":"abc123...","timestamp":"2026-06-12T14:30:00Z"}
# event: endpoint
# data: /mcp/rpc?sessionId=abc123...

# Terminal 3: List tools
SESSION="abc123..."  # Use sessionId from Terminal 2
curl -X POST "http://localhost:5110/mcp/rpc?sessionId=$SESSION" \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# Terminal 2 shows:
# event: ack
# data: {"id":1}
# event: message
# data: {"jsonrpc":"2.0","id":1,"result":{"tools":[{"name":"Todo.create","description":"Create a new Todo",...}]}}

# Terminal 3: Create a todo
curl -X POST "http://localhost:5110/mcp/rpc?sessionId=$SESSION" \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc":"2.0",
    "id":2,
    "method":"tools/call",
    "params":{
      "name":"Todo.create",
      "arguments":{"title":"Ship MCP HTTP","priority":1}
    }
  }'

# Terminal 2 shows:
# event: message
# data: {"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"\"01JB...\""}]}}
```

**What just happened?**
- Server exposed CRUD operations for Todo as MCP tools
- Client opened SSE stream and received session ID
- Client invoked `tools/list` via JSON-RPC → received tool descriptors
- Client invoked `tools/call` with `Todo.create` → entity created, response streamed
- SSE maintains persistent connection for real-time responses

**Pro tip:** The `[McpEntity]` attribute automatically generates CRUD tools (create, read, update, delete, list, query). Set `AllowMutations = false` to expose only read operations.

---

## 2. Understanding MCP Architecture

**Concept:** MCP (Model Context Protocol) standardizes how AI agents discover and invoke backend operations. Think of it as "function calling for remote systems."

### MCP Components

```
AI Agent (Claude, Cursor, etc.)
       ↓
MCP Client Library
       ↓
HTTP+SSE Transport
       ↓
Koan MCP Server
       ↓
Entity<T> Operations (CRUD)
       ↓
Data Provider (PostgreSQL, MongoDB, etc.)
```

### How HTTP+SSE Works

**Connection lifecycle:**
```
1. Client → GET /mcp/sse
2. Server → SSE stream opens, sends "connected" event with sessionId, then sends "endpoint" event with the POST target URI
3. Client → POST /mcp/rpc?sessionId=... (or with X-Mcp-Session header)
4. Server → Sends "ack" event (request acknowledged)
5. Server → Sends "message" event (containing JSON-RPC response payload)
6. Server → Sends periodic "heartbeat" events (keep connection alive)
7. Client → Closes stream or times out
8. Server → Cleans up session (sends "end" event on clean close)
```

### JSON-RPC Request Format

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "Todo.create",
    "arguments": {
      "title": "My task",
      "priority": 1
    }
  }
}
```

### SSE Response Format

```
event: ack
data: {"id":1}

event: message
data: {"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"Created Todo"}]}}

event: heartbeat
data: {"timestamp":"2026-06-12T14:35:00Z"}
```

### Recipe: MCP entity with custom operations

```csharp
[McpEntity(Description = "Task management with AI assistance", AllowMutations = true)]
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public int Priority { get; set; } = 3;
    public string? AiSuggestion { get; set; }

    // Custom MCP tool: analyze task complexity
    [McpTool(Description = "Analyze task complexity and suggest priority")]
    public static async Task<string> AnalyzeComplexity(string title)
    {
        // Call AI service to analyze
        var complexity = await AnalyzeWithAI(title);
        return $"Suggested priority: {complexity.SuggestedPriority}, Estimated time: {complexity.Hours}h";
    }
}
```

### Usage Scenarios

**Scenario 1: GitHub Copilot integration**
- Copilot connects to your Koan backend via HTTP+SSE
- Developer types "Create a high-priority todo for code review"
- Copilot invokes `Todo.create` with parsed arguments
- Task created in your system, Copilot shows confirmation

**Scenario 2: Custom GPT accessing enterprise data**
- Custom GPT connects with OAuth token
- User asks "What are my incomplete tasks?"
- GPT invokes `Todo.query` with filter `isCompleted=false`
- Results streamed back, GPT formats for user

**Scenario 3: Admin dashboard**
- React admin UI connects via browser SSE
- User clicks "Archive completed tasks"
- UI invokes `Todo.bulkUpdate` via MCP
- Real-time progress updates via SSE events

**Pro tip:** MCP tools are discoverable—AI agents call `tools/list` to learn what operations are available. Design tool names and descriptions to be self-explanatory.

---

## 3. Entity Exposure and Tool Generation

**Concept:** `[McpEntity]` attributes automatically generate CRUD tools. `[McpTool]` attributes expose custom static methods.

### Recipe: Basic CRUD exposure

```csharp
[McpEntity(
    Description = "Project management tasks",
    AllowMutations = true)]
public class Task : Entity<Task>
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Open;
    public int Priority { get; set; } = 3;
}

// Generated tools:
// - Task.create(title, description, status, priority)
// - Task.read(id)
// - Task.update(id, title?, description?, status?, priority?)
// - Task.delete(id)
// - Task.list(skip?, take?)
// - Task.query(filter)
```

### Recipe: Read-only entities

```csharp
[McpEntity(
    Description = "System metrics (read-only)",
    AllowMutations = false)]  // ← No create/update/delete tools
public class Metric : Entity<Metric>
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
    public DateTime Timestamp { get; set; }
}

// Generated tools:
// - Metric.read(id)
// - Metric.list(skip?, take?)
// - Metric.query(filter)
```

### Recipe: Hiding internal / PII fields (`[McpIgnore]`)

`[McpEntity]` exposes **every public property** of the entity: the auto-generated input schema lists them all, and tool results (and Code Mode results) serialize the raw entity. If your aggregate carries internal bookkeeping or PII — owner ids, provenance, per-source snapshots — those fields leak to any MCP client. `AllowMutations = false` only removes *write* tools; it does **not** stop the read result from returning every field.

Mark a property with `[McpIgnore]` to scope it out. The attribute is honored consistently across all three surfaces: the input schema, tool results (Tools **and** Code Mode), and input deserialization.

```csharp
using Koan.Mcp;

[McpEntity(Description = "Public catalog", AllowMutations = false)]
public class Work : Entity<Work>
{
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";

    [McpIgnore]                                  // hidden from schema AND results, and never settable
    public List<string> ClaimedByUserIds { get; set; } = new();   // PII

    [McpIgnore]
    public Dictionary<string, object> FieldProvenance { get; set; } = new();   // internal bookkeeping

    [McpIgnore(McpFieldDirection.Input)]         // server-owned: still returned, but callers cannot set it
    public string? SourceSightingId { get; set; }

    [McpIgnore(McpFieldDirection.Output)]        // accepted on write, but never echoed back
    public string? ImportToken { get; set; }
}
```

**Direction control** — `McpFieldDirection` selects where the property is excluded:

| Annotation | Input schema | Tool / Code Mode result | Settable by caller |
|------------|--------------|-------------------------|--------------------|
| `[McpIgnore]` (`Both`, default) | omitted | omitted | no |
| `[McpIgnore(McpFieldDirection.Output)]` | present | omitted | yes |
| `[McpIgnore(McpFieldDirection.Input)]` | omitted | present | no |
| *(no attribute)* | present | present | yes |

Exclusion also blocks the field on JSON-Patch (`patch`) tools: a patch operation whose path targets an input-excluded property is rejected before it runs.

**Why a dedicated attribute and not `[JsonIgnore]`?** Newtonsoft is Koan's canonical serializer for *both* the wire and persistence, so a `[Newtonsoft.Json.JsonIgnore]` would also drop the field from the data store. A `[System.Text.Json.Serialization.JsonIgnore]` is silently ignored by the Newtonsoft-based MCP serializer and would leak. `[McpIgnore]` is MCP-local: it changes nothing about how the entity is stored or how REST serializes it.

> **Note:** `[McpIgnore]` is a static, per-type decision (it is baked into the cached tool schema). It is **not** a per-caller / role-based filter — every MCP client sees the same shape. To vary visibility by caller, use the entity's `[Access]` gate + an `EntityAccess<T>` Constrain (which narrows rows per caller, WEB-0068), or expose a separate read-model entity.

> Default-deny posture: `[McpIgnore]` is a denylist (everything is exposed unless annotated), which matches Koan's expose-by-convention model. For a public surface, audit new properties as they are added — a freshly added field is exposed until someone marks it.

### Recipe: Custom tools

```csharp
[McpEntity(Description = "Customer management", AllowMutations = true)]
public class Customer : Entity<Customer>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public decimal LifetimeValue { get; set; }

    [McpTool(Description = "Find high-value customers above threshold")]
    public static async Task<List<Customer>> FindHighValue(decimal minValue)
    {
        return await Customer
            .Query(c => c.LifetimeValue >= minValue)
            .OrderByDescending(c => c.LifetimeValue)
            .Take(10)
            .ToListAsync();
    }

    [McpTool(Description = "Send welcome email to new customer")]
    public static async Task<string> SendWelcomeEmail(string customerId)
    {
        var customer = await Customer.Get(customerId);
        if (customer == null) return "Customer not found";

        await EmailService.SendWelcome(customer.Email);
        return $"Welcome email sent to {customer.Email}";
    }
}

// Generated tools include:
// - Customer.create, .read, .update, .delete, .list, .query (CRUD)
// - Customer.FindHighValue(minValue)
// - Customer.SendWelcomeEmail(customerId)
```

### Sample: Tool invocation from AI agent

```json
// AI agent discovers tools
POST /mcp/rpc
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list"
}

// Response:
{
  "id": 1,
  "result": {
    "tools": [
      {
        "name": "Customer.FindHighValue",
        "description": "Find high-value customers above threshold",
        "inputSchema": {
          "type": "object",
          "properties": {
            "minValue": {"type": "number"}
          },
          "required": ["minValue"]
        }
      }
    ]
  }
}

// AI agent invokes tool
POST /mcp/rpc
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "Customer.FindHighValue",
    "arguments": {"minValue": 10000}
  }
}

// Response (SSE stream):
event: message
data: {"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"Found 7 high-value customers: ..."}]}}
```

### Usage Scenarios

**Scenario 1: Sales assistant**
```csharp
[McpEntity(Description = "Sales opportunities", AllowMutations = true)]
public class Opportunity : Entity<Opportunity>
{
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime CloseDate { get; set; }

    [McpTool(Description = "Forecast revenue for next quarter")]
    public static async Task<decimal> ForecastRevenue()
    {
        var nextQuarter = DateTime.UtcNow.AddMonths(3);
        var opportunities = await Opportunity
            .Query(o => o.CloseDate <= nextQuarter)
            .ToListAsync();
        return opportunities.Sum(o => o.Amount);
    }
}

// AI agent: "What's our revenue forecast for next quarter?"
// → Invokes Opportunity.ForecastRevenue()
// → Returns "$1.2M expected from 15 opportunities"
```

**Scenario 2: DevOps assistant**
```csharp
[McpEntity(Description = "Deployment tracking", AllowMutations = true)]
public class Deployment : Entity<Deployment>
{
    public string Service { get; set; } = "";
    public string Version { get; set; } = "";
    public DeploymentStatus Status { get; set; }

    [McpTool(Description = "Check deployment health for service")]
    public static async Task<string> CheckHealth(string service)
    {
        var latest = await Deployment
            .Query(d => d.Service == service)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (latest == null) return $"No deployments found for {service}";
        return $"{service} v{latest.Version}: {latest.Status}";
    }
}

// AI agent: "Is the auth-service deployment healthy?"
// → Invokes Deployment.CheckHealth("auth-service")
// → Returns "auth-service v2.1.3: Healthy"
```

**Scenario 3: Content moderation**
```csharp
[McpEntity(Description = "User-generated content", AllowMutations = false)]
public class Post : Entity<Post>
{
    public string Content { get; set; } = "";
    public string Author { get; set; } = "";
    public ModerationStatus Status { get; set; }

    [McpTool(Description = "Flag post for moderation review")]
    public static async Task<string> FlagForReview(string postId, string reason)
    {
        var post = await Post.Get(postId);
        if (post == null) return "Post not found";

        post.Status = ModerationStatus.UnderReview;
        await post.Save();

        await ModerationQueue.Add(postId, reason);
        return $"Post {postId} flagged for review: {reason}";
    }
}

// AI agent: "This post contains spam, flag it"
// → Invokes Post.FlagForReview(postId, "spam")
// → Post queued for human review
```

**Pro tip:** Use `[McpTool]` for business logic, not data access. CRUD operations are auto-generated. Custom tools should encapsulate domain workflows.

---

## 4. Session Management

**Concept:** Each SSE connection creates a session. The session ID routes JSON-RPC requests to the correct stream.

### How Sessions Work

```
1. Client opens SSE: GET /mcp/sse
2. Server creates session (ID: abc123, TTL: 30min)
3. Server sends: event: connected, data: {"sessionId":"abc123","timestamp":"..."}
4. Server sends: event: endpoint, data: "/mcp/rpc?sessionId=abc123"
5. Client stores sessionId and endpoint URL
6. Client sends RPC: POST /mcp/rpc?sessionId=abc123 (or with X-Mcp-Session: abc123 header)
7. Server routes request to session abc123's SSE stream
8. Server sends: event: message (back to client via SSE containing the JSON-RPC response)
9. Heartbeats every 30s reset session TTL
10. Client disconnects or idle timeout → session cleaned up (sends event "end")
```

### Recipe: Session configuration

```json
{
  "Koan": {
    "Mcp": {
      "EnableLegacySseTransport": true,
      "SessionTimeoutMinutes": 30,
      "HeartbeatIntervalSeconds": 30,
      "MaxConcurrentConnections": 500,
      "CleanupIntervalMinutes": 5
    }
  }
}
```

### Recipe: Client-side session management

```typescript
// TypeScript MCP client
class McpClient {
  private sessionId: string | null = null;
  private eventSource: EventSource | null = null;

  async connect(baseUrl: string) {
    // Open SSE stream
    this.eventSource = new EventSource(`${baseUrl}/mcp/sse`);

    // Wait for session ID
    await new Promise<void>((resolve) => {
      this.eventSource!.addEventListener('connected', (e) => {
        const data = JSON.parse(e.data);
        this.sessionId = data.sessionId;
        console.log('Connected with session:', this.sessionId);
        resolve();
      });
    });

    // Handle results
    this.eventSource.addEventListener('result', (e) => {
      const data = JSON.parse(e.data);
      console.log('Result:', data);
    });

    // Handle errors
    this.eventSource.addEventListener('error', (e) => {
      const data = JSON.parse(e.data);
      console.error('Error:', data);
    });
  }

  async callTool(name: string, args: any) {
    if (!this.sessionId) throw new Error('Not connected');

    const response = await fetch(`${baseUrl}/mcp/rpc`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'X-Mcp-Session': this.sessionId
      },
      body: JSON.stringify({
        jsonrpc: '2.0',
        id: Date.now(),
        method: 'tools/call',
        params: { name, arguments: args }
      })
    });

    // Response arrives via SSE, not HTTP response body
    return response.ok;
  }
}

// Usage
const client = new McpClient();
await client.connect('http://localhost:5110');
await client.callTool('Todo.create', { title: 'Test', priority: 1 });
```

### Sample: Multiple concurrent sessions

```csharp
// Server handles multiple clients simultaneously
// Session A: Alice's agent
GET /mcp/sse → sessionId: alice-abc123

// Session B: Bob's agent
GET /mcp/sse → sessionId: bob-def456

// Both can invoke tools concurrently
POST /mcp/rpc (X-Mcp-Session: alice-abc123) → routed to Alice's SSE
POST /mcp/rpc (X-Mcp-Session: bob-def456) → routed to Bob's SSE

// Sessions are isolated (different users, different data contexts)
```

### Usage Scenarios

**Scenario 1: Idle timeout and reconnection**
```typescript
// Client detects disconnect
eventSource.onerror = async () => {
  console.log('Connection lost, reconnecting...');
  await reconnect();
};

async function reconnect() {
  // Close old connection
  eventSource?.close();

  // Wait before retry (exponential backoff)
  await sleep(retryDelay);
  retryDelay = Math.min(retryDelay * 2, 60000);

  // Reconnect
  await connect(baseUrl);
}
```

**Scenario 2: Session affinity for stateful operations**
```csharp
// Server maintains session-specific context
public class SessionContext
{
    public string UserId { get; set; }
    public string TenantId { get; set; }
    public Dictionary<string, object> State { get; set; } = new();
}

// Pipeline contributor injects session context
pipeline.AddStep(Phase.Intake, (context, _) =>
{
    var session = SessionManager.Get(context.SessionId);
    context.Set("userId", session.UserId);
    context.Set("tenantId", session.TenantId);
    return ValueTask.CompletedTask;
});
```

**Scenario 3: Graceful shutdown**
```csharp
// Server draining connections before shutdown
public async Task DrainConnections()
{
    // Stop accepting new connections
    _acceptingConnections = false;

    // Send shutdown notice to all active sessions
    foreach (var session in _activeSessions.Values)
    {
        await session.SendEvent("shutdown", new
        {
            message = "Server shutting down, please reconnect",
            gracePeriodSeconds = 30
        });
    }

    // Wait for clients to disconnect
    await Task.Delay(TimeSpan.FromSeconds(30));

    // Force close remaining connections
    foreach (var session in _activeSessions.Values)
    {
        await session.Close();
    }
}
```

**Pro tip:** Session IDs are opaque tokens—don't embed user data in them. Use session-scoped storage for user context, tenant routing, and request state.

---

## 5. Authentication and Security

**Concept:** Production MCP servers require authentication. Koan supports OAuth tokens, API keys, and custom auth schemes.

### Recipe: Enable authentication

```json
{
  "Koan": {
    "Mcp": {
      "EnableLegacySseTransport": true,
      "RequireAuthentication": true
    }
  }
}
```

`RequireAuthentication: true` gates the whole HTTP/SSE **transport** — an unauthenticated connection is refused before any tool runs. **Per-entity access is NOT MCP config** — it is the entity's `[Access]` gate ([SEC-0004](../decisions/SEC-0004-capability-authorization-gate-constrain-project.md)), the *same* gate REST enforces, declared on the type:

```csharp
[McpEntity(Name = "todo")]
[Access(write: "has:scope:todos:write", remove: "is:admin")]   // anyone reads; a scope writes; admin removes
public sealed class Todo : Entity<Todo> { public string Title { get; set; } = ""; }
```

A verb the caller lacks the grant for is absent from `tools/list` and denied on call (its denial rides back as `meta.shortCircuit`). Custom `[McpTool]` verbs gate with their own `RequiredScopes`. Full model: [authorization-howto.md](authorization-howto.md).

### Recipe: the embedded Authorization Server (the on-ramp)

The canonical path is **Reference = Intent**: reference `Koan.Web.Auth.Server` and `/mcp` becomes an OAuth 2.1 **resource server** that validates the framework-issued ES256 bearer tokens via the `Koan.bearer` scheme — **no `AddAuthentication` / `AddJwtBearer` / `UseAuthentication` ceremony**. The same package activates the Authorization Server at `/oauth/…` that mints those tokens (Authorization Code + PKCE, the RFC 8628 device grant, RFC 7591 DCR, rotating refresh) plus the RFC 9728/8414 discovery documents.

```xml
<PackageReference Include="Sylin.Koan.Web.Auth.Server" />
```

```csharp
// Program.cs — that's the whole wiring.
builder.Services.AddKoan();
```

```jsonc
"Koan": { "Mcp": {
  "EnableLegacySseTransport": true,
  "RequireAuthentication": true,
  "ResourceUri": "https://app.example.com/mcp"
} }
```

The app implements only **two pages** (consent + done). The full flow, the consent-seam contract, and the config live in **[oauth-server-howto.md](oauth-server-howto.md)**.

#### The /mcp resource-server edge

Without a valid token the edge returns `401` with an RFC 9728 challenge — `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource/mcp"` — and serves that protected-resource metadata so the client can discover the AS. It enforces the **per-resource audience** (RFC 8707): a token whose `aud` is not this resource is rejected even though its signature is valid (the confused-deputy fix). `Koan:Mcp:ResourceUri` is the canonical resource id — authoritative when set (host-spoof-proof behind a proxy); in Development it derives from the request host. Once the bearer identity is in `context.User`, the `[Access]` gate runs exactly as for REST.

#### Test it locally: `/oauth/dev-token`

In Development only (a hard 404 elsewhere), mint a token for the current cookie user and exercise the authenticated transport without a real OAuth client:

```bash
curl -b cookies.txt "http://localhost:5000/oauth/dev-token"           # after signing in via the app
curl -H "Authorization: Bearer <token>" "http://localhost:5000/mcp/sse"
```

### Recipe: validate tokens from an external IdP (advanced)

If you already run your own IdP and want the MCP edge to trust *its* tokens instead of the embedded AS, register a bearer scheme yourself:

```csharp
builder.Services.AddKoan();

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = "https://auth.example.com";
        options.Audience = "koan-mcp-api";
    });
```

> Either way, no named ASP.NET policies for entity access — the validated token's `scope`/role claims feed the entity `[Access]` gate directly (`[Access(write: "has:scope:todos:write")]`). You register (or reference) the *scheme*; the gate does the *authorization*.

### Recipe: API key authentication

```csharp
// Custom API key authentication
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
        {
            return AuthenticateResult.Fail("Missing API key");
        }

        var user = await _apiKeyService.ValidateAsync(apiKey);
        if (user == null)
        {
            return AuthenticateResult.Fail("Invalid API key");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim("scope", "todos:write")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}

// Register
builder.Services.AddAuthentication("ApiKey")
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);
```

### Recipe: CORS configuration

```json
{
  "Koan": {
    "Mcp": {
      "EnableCors": true,
      "AllowedOrigins": [
        "https://ide.example.com",
        "https://dashboard.example.com"
      ],
      "AllowedHeaders": ["Content-Type", "X-Mcp-Session", "Authorization"],
      "AllowCredentials": true
    }
  }
}
```

### Sample: Authenticated client

```typescript
// Client with OAuth token
const client = new McpClient();
await client.connect('https://api.example.com', {
  headers: {
    'Authorization': 'Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'
  }
});

await client.callTool('Todo.create', { title: 'Secure task' });
```

### Usage Scenarios

**Scenario 1: Multi-tenant isolation**
```csharp
// Extract tenant from JWT token
public class TenantMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantClaim = context.User.FindFirst("tenant_id");
        if (tenantClaim != null)
        {
            // Scope all entity operations to tenant
            using (EntityContext.Partition(tenantClaim.Value))
            {
                await _next(context);
            }
        }
        else
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync("Missing tenant claim");
        }
    }
}

// All MCP tool invocations now scoped to tenant
```

**Scenario 2: Role-based tool access**
```csharp
[McpEntity(Description = "User management", AllowMutations = true)]
[RequireRole("admin")]  // Entity-level restriction
public class User : Entity<User>
{
    public string Email { get; set; } = "";
    public string Role { get; set; } = "user";

    [McpTool(Description = "Promote user to admin")]
    [RequireRole("super-admin")]  // Tool-level restriction
    public static async Task<string> PromoteToAdmin(string userId)
    {
        var user = await User.Get(userId);
        if (user == null) return "User not found";

        user.Role = "admin";
        await user.Save();

        return $"User {user.Email} promoted to admin";
    }
}

// Regular admin: Can access User entity, but NOT PromoteToAdmin
// Super admin: Can access everything
```

**Scenario 3: Rate limiting**
```csharp
// Per-user rate limiting
public class RateLimitMiddleware
{
    private readonly Dictionary<string, RateLimiter> _limiters = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            await _next(context);
            return;
        }

        var limiter = _limiters.GetOrAdd(userId, _ => new RateLimiter(
            maxRequests: 100,
            windowMinutes: 1));

        if (!limiter.TryAcquire())
        {
            context.Response.StatusCode = 429;
            await context.Response.WriteAsync("Rate limit exceeded");
            return;
        }

        await _next(context);
    }
}
```

**Pro tip:** Always use HTTPS in production. HTTP+SSE exposes session IDs and auth tokens—encrypt in transit with TLS.

---

## 6. Capability Discovery

**Concept:** The `/mcp/capabilities` endpoint exposes transport metadata for dashboards and managed clients.

### Recipe: Enable capability endpoint

```json
{
  "Koan": {
    "Mcp": {
      "PublishCapabilityEndpoint": true
    }
  }
}
```

### Sample: Capabilities response

```bash
curl http://localhost:5110/mcp/capabilities | jq
```

```json
{
  "serverInfo": {
    "name": "koan-mcp-server",
    "version": "0.17.0"
  },
  "capabilities": {
    "tools": {
      "supported": true,
      "listChanges": false
    },
    "resources": {
      "supported": false
    },
    "prompts": {
      "supported": false
    }
  },
  "transport": {
    "type": "http-sse",
    "endpoints": {
      "sse": "/mcp/sse",
      "rpc": "/mcp/rpc",
      "capabilities": "/mcp/capabilities"
    },
    "authentication": {
      "required": false,
      "schemes": ["Bearer"]
    },
    "cors": {
      "enabled": true,
      "allowedOrigins": ["http://localhost:3000"]
    }
  },
  "tools": [
    {
      "name": "Todo.create",
      "description": "Create a new Todo",
      "inputSchema": {
        "type": "object",
        "properties": {
          "title": {"type": "string"},
          "isCompleted": {"type": "boolean"},
          "priority": {"type": "number"}
        },
        "required": ["title"]
      }
    },
    {
      "name": "Todo.read",
      "description": "Read a Todo by ID",
      "inputSchema": {
        "type": "object",
        "properties": {
          "id": {"type": "string"}
        },
        "required": ["id"]
      }
    }
  ]
}
```

### Usage Scenarios

**Scenario 1: Admin dashboard**
```typescript
// Fetch capabilities on dashboard load
async function loadServerInfo() {
  const capabilities = await fetch('/mcp/capabilities').then(r => r.json());

  // Display server status
  console.log(`Server: ${capabilities.serverInfo.name} v${capabilities.serverInfo.version}`);
  console.log(`Tools available: ${capabilities.tools.length}`);
  console.log(`Authentication: ${capabilities.transport.authentication.required ? 'Required' : 'Optional'}`);

  // Build tool catalog
  renderToolCatalog(capabilities.tools);
}
```

**Scenario 2: Client auto-configuration**
```typescript
// Client discovers endpoints from capabilities
class McpClient {
  async configure(baseUrl: string) {
    const capabilities = await fetch(`${baseUrl}/mcp/capabilities`).then(r => r.json());

    this.sseEndpoint = `${baseUrl}${capabilities.transport.endpoints.sse}`;
    this.rpcEndpoint = `${baseUrl}${capabilities.transport.endpoints.rpc}`;
    this.authRequired = capabilities.transport.authentication.required;

    console.log('Client configured from server capabilities');
  }
}
```

**Scenario 3: Health monitoring**
```csharp
// Health check endpoint using capabilities
app.MapGet("/health", async (HttpContext context) =>
{
    var mcpHealth = await CheckMcpHealth();

    return Results.Ok(new
    {
        status = mcpHealth.IsHealthy ? "healthy" : "degraded",
        mcp = new
        {
            transport = "http-sse",
            activeConnections = mcpHealth.ActiveConnections,
            toolsAvailable = mcpHealth.ToolCount
        }
    });
});
```

**Pro tip:** Use capabilities endpoint for monitoring dashboards. Track `activeConnections`, `requestsPerMinute`, and `toolInvocationErrors` to detect issues.

---

## 7. Advanced Patterns

### Pattern: Streaming large results

```csharp
[McpEntity(Description = "Large dataset access", AllowMutations = false)]
public class Report : Entity<Report>
{
    public string Title { get; set; } = "";
    public DateTime GeneratedAt { get; set; }

    [McpTool(Description = "Stream report data in chunks")]
    public static async IAsyncEnumerable<string> StreamReportData(string reportId)
    {
        var report = await Report.Get(reportId);
        if (report == null) yield break;

        // Stream data in chunks
        var dataSource = GetLargeDataSource(reportId);
        await foreach (var chunk in dataSource)
        {
            yield return chunk; // Each chunk sent as separate SSE event
        }
    }
}

// Client receives multiple SSE events:
// event: message
// data: {"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"Chunk 1"}]}}
// event: message
// data: {"jsonrpc":"2.0","id":1,"result":{"content":[{"type":"text","text":"Chunk 2"}]}}
// ...
```

### Pattern: Tool composition

```csharp
[McpEntity(Description = "Workflow automation", AllowMutations = true)]
public class Workflow : Entity<Workflow>
{
    public string Name { get; set; } = "";
    public List<WorkflowStep> Steps { get; set; } = new();

    [McpTool(Description = "Execute multi-step workflow")]
    public static async Task<string> ExecuteWorkflow(string workflowId)
    {
        var workflow = await Workflow.Get(workflowId);
        if (workflow == null) return "Workflow not found";

        var results = new List<string>();

        foreach (var step in workflow.Steps)
        {
            // Each step invokes another MCP tool
            var result = await InvokeTool(step.ToolName, step.Arguments);
            results.Add($"{step.ToolName}: {result}");
        }

        return string.Join("\n", results);
    }
}
```

### Pattern: Event notifications

```csharp
// Server pushes notifications via SSE (outside JSON-RPC)
public class NotificationService
{
    public async Task NotifySession(string sessionId, string message)
    {
        var session = SessionManager.Get(sessionId);
        if (session == null) return;

        await session.SendEvent("notification", new
        {
            type = "info",
            message,
            timestamp = DateTime.UtcNow
        });
    }
}

// Client receives:
// event: notification
// data: {"type":"info","message":"Task completed","timestamp":"2025-11-09T15:00:00Z"}
```

**Pro tip:** Use tool composition for complex workflows. Let AI agents orchestrate simple tools into sophisticated operations.

---

## 8. Performance Considerations

### Connection Limits

| Configuration | Recommended Value | Notes |
|---------------|-------------------|-------|
| MaxConcurrentConnections | 500 | Per server instance |
| SessionTimeoutMinutes | 30 | Balance responsiveness vs resource usage |
| HeartbeatIntervalSeconds | 30 | Prevent idle timeouts |
| CleanupIntervalMinutes | 5 | Session garbage collection |

### Throughput Benchmarks

| Scenario | Throughput | Latency (p50) | Latency (p99) |
|----------|-----------|---------------|---------------|
| Simple tool call (Todo.read) | 1,000 req/sec | 5ms | 15ms |
| Complex query (filter + join) | 200 req/sec | 25ms | 100ms |
| Streaming large result | 50 streams/sec | N/A (chunked) | N/A |
| Multiple concurrent sessions | 500 sessions | 10ms | 30ms |

### Optimization Strategies

**Strategy 1: Connection pooling**
```json
{
  "Koan": {
    "Mcp": {
      "MaxConcurrentConnections": 1000,
      "ConnectionPoolSize": 100
    }
  }
}
```

**Strategy 2: Response caching**
```csharp
[McpTool(Description = "Get system stats (cached)")]
[ResponseCache(Duration = 60)] // Cache for 60 seconds
public static async Task<SystemStats> GetStats()
{
    return await ComputeExpensiveStats();
}
```

**Strategy 3: Horizontal scaling**
```
Load Balancer
    ↓
┌───┴────┐
↓        ↓
MCP      MCP
Server1  Server2
(500     (500
sessions) sessions)
```

**Pro tip:** Monitor SSE connection count. If approaching limits, scale horizontally or increase timeouts to reduce churn.

---

## 9. Troubleshooting

### Issue 1: Session Not Found (404)

**Symptoms:**
```
POST /mcp/rpc → 404 Not Found
{"error":"Session not found"}
```

**Causes:**
- Session expired (idle > timeout)
- Wrong session ID in `X-Mcp-Session` header
- SSE connection closed

**Solutions:**
```bash
# BAD: Using old/wrong session ID
curl -X POST http://localhost:5110/mcp/rpc \
  -H "X-Mcp-Session: wrong-id-123"

# GOOD: Use current session ID from SSE stream
# Terminal 1: Get session ID
curl -N http://localhost:5110/mcp/sse
# event: connected
# data: {"sessionId":"abc123..."}

# Terminal 2: Use that session ID
SESSION="abc123..."
curl -X POST http://localhost:5110/mcp/rpc \
  -H "X-Mcp-Session: $SESSION" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
```

**Debug tip:** Check session timeout in config. Increase if clients frequently lose sessions.

---

### Issue 2: CORS Error

**Symptoms:**
```
Access to fetch at 'http://localhost:5110/mcp/rpc' from origin 'http://localhost:3000'
has been blocked by CORS policy
```

**Causes:**
- `EnableCors` not set to true
- Origin not in `AllowedOrigins` list
- Missing required headers in `AllowedHeaders`

**Solutions:**
```json
// BAD: CORS disabled
{
  "Koan": {
    "Mcp": {
      "EnableCors": false
    }
  }
}

// GOOD: CORS enabled with allowed origins
{
  "Koan": {
    "Mcp": {
      "EnableCors": true,
      "AllowedOrigins": [
        "http://localhost:3000",
        "https://app.example.com"
      ],
      "AllowedHeaders": [
        "Content-Type",
        "X-Mcp-Session",
        "Authorization"
      ],
      "AllowCredentials": true
    }
  }
}
```

**Debug tip:** Use browser DevTools Network tab to see CORS preflight (OPTIONS) requests. Ensure server responds with correct `Access-Control-*` headers.

---

### Issue 3: Authentication Failed (401)

**Symptoms:**
```
POST /mcp/rpc → 401 Unauthorized
{"error":"Missing or invalid authentication"}
```

**Causes:**
- `RequireAuthentication: true` but no token provided
- Invalid/expired JWT token
- Wrong authentication scheme

**Solutions:**
```bash
# BAD: Missing auth token
curl -X POST http://localhost:5110/mcp/rpc \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# GOOD: Include auth token
curl -X POST http://localhost:5110/mcp/rpc \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..." \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'

# Check token expiration
jwt decode eyJhbGciOiJIUzI1NiIs... | jq .exp
# Ensure exp > current time
```

**Debug tip:** Enable authentication logging to see why tokens are rejected:
```csharp
builder.Services.AddLogging(logging =>
{
    logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Debug);
});
```

---

### Issue 4: SSE Stream Disconnects

**Symptoms:**
- SSE connection closes after few minutes
- Client receives no heartbeats

**Causes:**
- Proxy/load balancer timeout (common with Nginx, CloudFlare)
- Server-side timeout too short
- Network issue

**Solutions:**
```json
// Increase heartbeat frequency
{
  "Koan": {
    "Mcp": {
      "HeartbeatIntervalSeconds": 15,  // Send heartbeat every 15s
      "SessionTimeoutMinutes": 60      // Longer session timeout
    }
  }
}
```

```nginx
# Nginx configuration for SSE
location /mcp/sse {
    proxy_pass http://backend;
    proxy_set_header Connection '';
    proxy_http_version 1.1;
    chunked_transfer_encoding off;
    proxy_buffering off;
    proxy_cache off;
    proxy_read_timeout 3600s;  # 1 hour
}
```

**Debug tip:** Monitor SSE events in browser DevTools EventSource tab. Check for gaps in heartbeat timing.

---

### Issue 5: Tool Not Found

**Symptoms:**
```
event: message
data: {"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"Tool not found: Todo.customTool"}}
```

**Causes:**
- Typo in tool name
- Entity not decorated with `[McpEntity]`
- Method not decorated with `[McpTool]`
- Method not static

**Solutions:**
```csharp
// BAD: Method not static
[McpEntity]
public class Todo : Entity<Todo>
{
    [McpTool]
    public async Task<string> CustomTool()  // ❌ Not static
    {
        return "result";
    }
}

// GOOD: Static method
[McpEntity]
public class Todo : Entity<Todo>
{
    [McpTool(Description = "Custom operation")]
    public static async Task<string> CustomTool()  // ✅ Static
    {
        return "result";
    }
}

// Verify tool is listed
curl -X POST http://localhost:5110/mcp/rpc \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | jq '.result.tools[].name'
```

**Debug tip:** Check server boot logs for entity/tool discovery. Koan logs all registered MCP tools at startup.

---

### Issue 6: High Memory Usage

**Symptoms:**
- Server memory grows over time
- Many idle sessions

**Causes:**
- Sessions not cleaned up (long timeout + high churn)
- Memory leaks in tool implementations
- Large response caching

**Solutions:**
```json
// Reduce session retention
{
  "Koan": {
    "Mcp": {
      "SessionTimeoutMinutes": 15,  // Shorter timeout
      "CleanupIntervalMinutes": 2,  // More frequent cleanup
      "MaxConcurrentConnections": 200  // Hard limit
    }
  }
}
```

```csharp
// Monitor session count
app.MapGet("/metrics", () => new
{
    activeSessions = SessionManager.ActiveCount,
    memoryMB = GC.GetTotalMemory(false) / 1024 / 1024
});
```

**Debug tip:** Use memory profiler (dotMemory, ANTS) to find leaks. Check for event handlers not unsubscribed when sessions close.

---

### Issue 7: Rate Limit Exceeded (429)

**Symptoms:**
```
POST /mcp/rpc → 429 Too Many Requests
```

**Causes:**
- Per-user rate limit hit
- Global rate limit hit
- DDoS protection triggered

**Solutions:**
```csharp
// Configure rate limiting
builder.Services.AddRateLimiting(options =>
{
    options.AddFixedWindowLimiter("mcp", limiter =>
    {
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.PermitLimit = 100;
        limiter.QueueLimit = 10;
    });
});

// Apply to MCP endpoints
app.UseRateLimiter();

// Check rate limit headers
curl -i http://localhost:5110/mcp/rpc
# X-RateLimit-Limit: 100
# X-RateLimit-Remaining: 42
# X-RateLimit-Reset: 1699564800
```

**Debug tip:** Add retry logic with exponential backoff. Respect `Retry-After` header if server provides it.

---

## 10. Summary and Next Steps

You've now mastered MCP over HTTP+SSE—from exposing entities as tools to handling authentication, sessions, and real-time streaming.

**Key Takeaways:**
1. **HTTP+SSE for remote agents** - Use when AI agents run in cloud/browser
2. **`[McpEntity]` auto-generates CRUD** - CRUD tools created automatically
3. **`[McpTool]` for custom logic** - Expose domain workflows as discoverable tools
4. **Sessions route requests** - Each SSE connection gets unique session ID
5. **Authentication required in production** - OAuth, API keys, or custom schemes

**Choosing Your Transport:**
- **Remote agents** → HTTP+SSE (this guide)
- **Local agents** → STDIO (simpler, no network config)
- **Browser clients** → HTTP+SSE (CORS support)
- **Horizontal scaling** → HTTP+SSE (stateless sessions)

**Security Checklist:**
- ✅ Enable HTTPS in production
- ✅ Require authentication (`RequireAuthentication: true`)
- ✅ Configure CORS for browser clients
- ✅ Set per-entity authorization (scopes, roles)
- ✅ Mark internal / PII fields with `[McpIgnore]` — `[McpEntity]` exposes every public property in results by default (see [Hiding internal / PII fields](#recipe-hiding-internal--pii-fields-mcpignore))
- ✅ Implement rate limiting
- ✅ Monitor session count and memory usage

**Common Patterns:**
- Cloud IDE integration → OAuth + CORS
- Hosted AI agents → API key auth
- Admin dashboards → Session-based auth + CORS
- Multi-tenant SaaS → Tenant isolation via partition context

**Next Steps:**
1. **Deploy to production** - Enable HTTPS, authentication, rate limiting
2. **Monitor performance** - Track session count, request latency, error rate
3. **Build custom tools** - Use `[McpTool]` for domain workflows
4. **Integrate with AI agents** - Connect Claude, Cursor, custom GPTs
5. **Read related guides:**
   - [Entity Capabilities](entity-capabilities-howto.md) - Entity patterns for MCP tools
   - [Patch Capabilities](patch-capabilities-howto.md) - Partial updates via MCP
   - [Canon Capabilities](canon-capabilities-howto.md) - Multi-source aggregation via MCP

**Questions or Issues?**
- Check [Troubleshooting](#9-troubleshooting) section above
- Review [MCP-0001](../archive/proposals/koan-mcp-http-sse-transport.md) for architecture details
- See [Model Context Protocol](https://modelcontextprotocol.io) specification

Remember: MCP makes your backend operations discoverable to AI agents. Design tool names and descriptions to be self-explanatory—agents call `tools/list` to learn what's possible, then compose tools into sophisticated workflows.
