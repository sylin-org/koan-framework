---
id: AI-0031
slug: AI-0031-entity-aware-agents
domain: AI
status: Proposed
date: 2026-03-20
---

# ADR: Entity-Aware Agents

**Contract**

- **Inputs:** Entity types registered via `.WithEntities<T>()`, vector-enabled entities via `.WithSearch<T>()`, optional custom tools via `.WithTools()`, agent memory configuration, system prompt or `Prompt` (AI-0025), planning strategy selection, iteration/token budget limits.
- **Outputs:** `AgentResult` containing the final response text, list of `AgentStep` (reasoning + tool calls + observations), total token usage, iteration count, and tool call audit trail. Streaming variant emits `AgentStep` as they occur.
- **Error Modes:** Max iterations exceeded: returns partial result with `AgentStatus.IterationLimitReached` and the reasoning so far (not an exception — the agent tried its best). Token budget exceeded: returns partial result with `AgentStatus.BudgetExhausted`. Tool execution failure: agent observes the error and can retry or reason around it (tool errors are observations, not fatals). No tools registered: throws `InvalidOperationException` at build time. AI provider unavailable: throws standard `Client` exceptions per AI-0021 behavior.
- **Acceptance Criteria:** `Agent.Create().WithEntities<Product>().Run("Find laptops under $500")` produces tool calls to `Product.Query()` and returns results without manual tool definition; entity tool schemas are derived from entity properties at compile time, not from docstrings; `AgentMemory.Entity<T>()` persists conversation state across process restarts; agents work with any AI source via `Client.Scope()`.

**Edge Cases**

- Entity with no public queryable properties: Agent receives a tool with only `Get(id)` — no `Query()` generated. Agent can still retrieve by ID if the user provides one.
- Entity with `[Embedding]` but no stored vectors: `Search` tool is registered but returns empty results. Agent observes "No results found" and can fall back to `Query()`.
- Tool returns large result set (e.g., `Product.Query()` returns 10,000 items): Tool results are truncated to configurable `MaxToolResultTokens` (default: 4,000 tokens). Agent observes truncation note and can refine the query.
- Circular tool calling (agent calls same tool repeatedly with same arguments): Detected after 3 identical calls. Agent receives observation: "Repeated tool call detected. Try a different approach." Counts against iteration budget.
- Agent calls `Entity.Create()` or `Entity.Save()` (mutating operation): Only exposed if `.WithEntities<T>(write: true)` is specified. Default is read-only. Mutation tools require explicit opt-in.
- Concurrent agents sharing `AgentMemory.Entity<T>()`: Entity concurrency handled by standard Koan entity conflict resolution. Each agent session has a unique conversation ID.
- Model doesn't support tool/function calling: Agent falls back to prompt-based tool invocation (structured output parsing). Works with any model, less reliable than native tool calling.

## Context

The AI-0022 vision deferred `Agent.*` pending a build-vs-interop decision. That decision is now resolved:

- **Graph/Workflow: not needed.** Koan's existing MCP infrastructure (AI-0012, AI-0013, AI-0014) exposes all facades as tools. External engines (LangGraph, Temporal, Prefect, Claude) consume Koan via MCP protocol. No Koan-side code needed for orchestration interop.
- **Agent: build thin.** The unique value is **entity-aware tool auto-generation** — something no external framework can provide. The reasoning loop itself (ReAct) is simple. The value is what's inside the loop.

An agent in Koan is a **Chain with a loop and auto-generated entity tools**. Chain.* (AI-0026) already provides: `.Chat()`, `.WithTools()`, `.WithMemory()`, `.System()`, `.Scope()`, `.Parse<T>()`. Agent adds: iteration control, planning strategy, and — critically — `.WithEntities<T>()`.

### Why Build

LangChain agents require manual tool definition:

```python
# LangChain: manually define each tool
@tool
def search_products(query: str, max_price: float = None) -> list:
    """Search products by query and optional max price."""
    # 20 lines of implementation
    return db.query(...)

@tool
def get_order(order_id: str) -> dict:
    """Get order by ID."""
    return db.get(...)

agent = create_react_agent(llm, [search_products, get_order])
```

Koan generates all of this from the entity definition:

```csharp
// Koan: zero tool definitions
var agent = Agent.Create()
    .WithEntities<Product, Order>()
    .Run("Find products similar to order #123");
```

The entity type IS the tool definition. Properties become schema fields. Static methods become operations. `[Embedding]` enables search. No duplication.

### Why Not Build More

Stateful multi-step workflows, multi-agent supervisors, and HITL with durable persistence are orchestration concerns. Koan exposes capabilities via MCP (AI-0012/0013/0014). External engines compose them:

```
LangGraph / Temporal / Claude
       │
       │  MCP protocol (existing)
       ▼
Koan MCP Server
       ├── SDK.Entities.*       (existing, AI-0014)
       ├── SDK.Model.*          (AI-0023)
       ├── SDK.Training.*       (AI-0028)
       ├── SDK.Eval.*           (AI-0029)
       └── SDK.Chain.*          (AI-0026)
```

Building a workflow engine would duplicate mature infrastructure. The agent is worth building because entity-aware tooling is the differentiator.

## Decision

### Part 1: Agent as Chain Extension

An agent is implemented as a specialized chain execution mode, not a separate engine. It reuses Chain's tool infrastructure, memory, scoping, and Client integration.

```csharp
public static class Agent
{
    public static AgentBuilder Create() => new();
}
```

The `AgentBuilder` composes the same primitives as `ChainBuilder` (AI-0026), adding iteration control and entity tool generation:

```csharp
public sealed class AgentBuilder
{
    // ── System prompt ──
    public AgentBuilder System(string systemPrompt);
    public AgentBuilder System(Prompt prompt);
    public AgentBuilder WithPrompt(string promptName);  // From catalog (AI-0025)

    // ── Entity tools (the differentiator) ──
    public AgentBuilder WithEntities<T>(bool write = false) where T : Entity<T>;
    public AgentBuilder WithEntities<T1, T2>(bool write = false)
        where T1 : Entity<T1> where T2 : Entity<T2>;
    public AgentBuilder WithEntities<T1, T2, T3>(bool write = false)
        where T1 : Entity<T1> where T2 : Entity<T2> where T3 : Entity<T3>;
    // ... up to 8 type parameters (convention matches Func<>/Action<>)

    // ── Vector search tools ──
    public AgentBuilder WithSearch<T>() where T : Entity<T>;

    // ── Custom tools (same as Chain) ──
    public AgentBuilder WithTools(params Tool[] tools);

    // ── Memory ──
    public AgentBuilder WithMemory(AgentMemory memory);

    // ── Planning ──
    public AgentBuilder WithPlanning(PlanStrategy strategy = PlanStrategy.ReAct);

    // ── Budgets ──
    public AgentBuilder WithMaxIterations(int max = 10);
    public AgentBuilder WithMaxTokens(int tokens = 100_000);
    public AgentBuilder WithMaxToolResultTokens(int tokens = 4_000);

    // ── Routing ──
    public AgentBuilder Scope(string? chat = null, string? embed = null);

    // ── Execution ──
    public Task<AgentResult> Run(string goal, CancellationToken ct = default);
    public Task<AgentResult> Run(string goal, object? context = null, CancellationToken ct = default);
    public IAsyncEnumerable<AgentStep> Stream(string goal, CancellationToken ct = default);
}
```

### Part 2: Entity Tool Generation

`.WithEntities<T>()` introspects the entity type at build time and generates tool definitions. This reuses the same reflection that MCP Code Mode (AI-0014) uses for TypeScript `.d.ts` generation.

**Read-only tools (default):**

| Tool Name | Generated From | Description |
|-----------|---------------|-------------|
| `{Type}_get` | `Entity<T>.Get(id)` | Retrieve entity by ID |
| `{Type}_query` | `Entity<T>.Query(filter)` | Query entities with filter expression |
| `{Type}_count` | `Entity<T>.Count(filter?)` | Count matching entities |

**Write tools (opt-in via `write: true`):**

| Tool Name | Generated From | Description |
|-----------|---------------|-------------|
| `{Type}_create` | `Entity<T>.Create(...)` | Create new entity |
| `{Type}_save` | `entity.Save()` | Update entity |
| `{Type}_delete` | `entity.Delete()` | Delete entity |

**Search tools (via `.WithSearch<T>()`):**

| Tool Name | Generated From | Description |
|-----------|---------------|-------------|
| `{Type}_search` | `Vector<T>.Search(text, topK)` | Semantic search via entity embeddings |

**Schema generation:**

Tool input schemas are derived from entity properties:

```csharp
// Given this entity:
public class Product : Entity<Product>
{
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public string Category { get; set; }
    public bool InStock { get; set; }
}

// Agent generates this tool schema:
// product_query:
//   filter: {
//     name: string?,
//     category: string?,
//     price_min: number?,
//     price_max: number?,
//     in_stock: boolean?
//   }
//   limit: integer (default: 20)
//   order_by: string? (property name)
//   order_desc: boolean (default: false)
```

The schema is type-safe — `Price` becomes a numeric range filter, `InStock` becomes a boolean, `Category` becomes a string match. No manual mapping.

**Tool result formatting:**

Entity results are serialized as concise JSON. Large collections are truncated to `MaxToolResultTokens`:

```json
{
  "results": [
    {"id": "p-001", "name": "ThinkPad X1", "price": 499.99, "category": "Laptop", "inStock": true},
    {"id": "p-002", "name": "MacBook Air", "price": 899.99, "category": "Laptop", "inStock": true}
  ],
  "total": 47,
  "truncated": true,
  "showing": 5
}
```

### Part 3: Planning Strategies

```csharp
public enum PlanStrategy
{
    ReAct,           // Reason → Act → Observe → Repeat (default)
    FunctionCalling, // Direct tool calls without explicit reasoning (faster, less transparent)
    PlanAndExecute   // Plan all steps first, then execute sequentially (more structured)
}
```

**ReAct (default):**

The standard reasoning loop. The model reasons about what to do, calls a tool, observes the result, and repeats:

```
System: You have access to these tools: [product_query, order_get, product_search, ...]

User: Find products similar to order #123 and suggest cheaper alternatives.

Assistant (thinking): I need to first look up order #123 to see what product was ordered.
Tool call: order_get(id: "123")
Observation: {id: "123", product_id: "p-045", product_name: "Sony WH-1000XM5", total: 349.99}

Assistant (thinking): The order contains Sony WH-1000XM5 headphones at $349.99.
I should search for similar products and filter for lower prices.
Tool call: product_search(text: "wireless noise cancelling headphones")
Observation: [{name: "Sony WH-1000XM5", price: 349.99}, {name: "Bose QC45", price: 279.99}, ...]

Assistant (thinking): Found similar headphones. Let me filter for cheaper alternatives.
Tool call: product_query(filter: {category: "Headphones", price_max: 349.99, in_stock: true}, order_by: "price")
Observation: [{name: "Bose QC45", price: 279.99}, {name: "Jabra Elite 85h", price: 249.99}, ...]

Final answer: Based on order #123 (Sony WH-1000XM5, $349.99), here are cheaper alternatives...
```

**FunctionCalling:** The model calls tools directly without explicit reasoning text. Faster, lower token usage, but less transparent. Best for models with strong native tool calling (GPT-4, Claude, Llama 3.1+).

**PlanAndExecute:** The model first generates a plan (list of steps), then executes each step sequentially. More structured, better for complex multi-step tasks, but slower. Two LLM calls per step (plan + execute).

### Part 4: Agent Memory

Three memory types, all leveraging existing Koan infrastructure:

```csharp
public abstract record AgentMemory
{
    /// In-memory sliding window. Ephemeral — lost on process restart.
    public static AgentMemory Sliding(int maxTurns = 20) => new SlidingMemory(maxTurns);

    /// Persisted as Entity<T>. Survives restarts. Queryable.
    public static AgentMemory Entity<T>() where T : Entity<T> => new EntityMemory<T>();

    /// Vector-backed. Retrieves semantically relevant past interactions.
    public static AgentMemory Semantic<T>() where T : Entity<T> => new SemanticMemory<T>();
}
```

**Sliding:** Keeps the last N turns in memory. Simple, no persistence. Appropriate for single-session agents (chatbots, one-off tasks).

**Entity:** Stores conversation state as a Koan entity. The entity type is user-defined — the framework writes `Messages`, `CreatedAt`, `SessionId` fields via convention detection. Conversations are queryable (`ConversationLog.Query(c => c.UserId == user)`), deletable, and survive process restarts.

```csharp
// User defines the memory entity:
public class ConversationLog : Entity<ConversationLog>
{
    public string SessionId { get; set; }
    public string? UserId { get; set; }
    public List<AgentMessage> Messages { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime LastActiveAt { get; set; }
}

// Agent uses it:
var agent = Agent.Create()
    .WithEntities<Product>()
    .WithMemory(AgentMemory.Entity<ConversationLog>());

// First call:
await agent.Run("I'm looking for running shoes", context: new { SessionId = "s-123" });
// ConversationLog entity created with session "s-123"

// Second call (same session — agent remembers):
await agent.Run("Something under $100", context: new { SessionId = "s-123" });
// Agent loads ConversationLog for "s-123", sees prior context about running shoes
```

**Semantic:** Combines entity persistence with vector search. Past interactions are embedded and stored. On each new turn, the framework retrieves the N most semantically relevant past messages — not just the most recent. The agent "remembers" relevant conversations from days ago if they match the current query.

```csharp
var agent = Agent.Create()
    .WithEntities<Product>()
    .WithMemory(AgentMemory.Semantic<ConversationLog>());

// Two weeks ago: user asked about laptop bags
// Today: user asks about "accessories for my new ThinkPad"
// Semantic memory retrieves the laptop bag conversation as relevant context
```

### Part 5: Result Types

```csharp
public sealed record AgentResult
{
    public string Text { get; init; }                        // Final response
    public AgentStatus Status { get; init; }                 // Completed, IterationLimitReached, BudgetExhausted
    public IReadOnlyList<AgentStep> Steps { get; init; }     // Full reasoning trace
    public int Iterations { get; init; }                     // Total loop count
    public int TotalTokens { get; init; }                    // Total tokens used
    public TimeSpan Duration { get; init; }                  // Wall clock time
}

public sealed record AgentStep
{
    public string? Reasoning { get; init; }                  // Model's thinking (ReAct only)
    public ToolCall? ToolCall { get; init; }                 // Tool invocation
    public string? Observation { get; init; }                // Tool result
    public int TokensUsed { get; init; }                     // Tokens for this step
}

public sealed record ToolCall(string Name, Dictionary<string, object?> Arguments);

public enum AgentStatus
{
    Completed,              // Agent reached a final answer
    IterationLimitReached,  // Hit max iterations, returning best effort
    BudgetExhausted         // Hit token budget, returning best effort
}
```

### Part 6: Streaming

Agents stream step-by-step, giving visibility into the reasoning process:

```csharp
await foreach (var step in agent.Stream("Find me a gift under $50"))
{
    if (step.Reasoning != null)
        Console.WriteLine($"Thinking: {step.Reasoning}");

    if (step.ToolCall != null)
        Console.WriteLine($"Calling: {step.ToolCall.Name}({step.ToolCall.Arguments})");

    if (step.Observation != null)
        Console.WriteLine($"Result: {step.Observation}");
}
```

For web applications, `app.MapAgent()` exposes an agent as an SSE endpoint:

```csharp
app.MapAgent("/api/assistant", agent);
// POST /api/assistant { "goal": "Find me a gift under $50" }
// → SSE stream of AgentStep events
```

### Part 7: MCP Tool Reuse

Entity tool generation reuses the same entity introspection that MCP Code Mode (AI-0014) already performs. The `IEntityToolGenerator` interface is shared:

```csharp
// Shared infrastructure — used by both Agent and MCP Code Mode
internal interface IEntityToolGenerator
{
    IReadOnlyList<ToolDefinition> GenerateTools<T>(EntityToolOptions options) where T : Entity<T>;
}

// MCP Code Mode uses it to generate TypeScript SDK:
// SDK.Entities.Product.get(id) → calls IEntityToolGenerator

// Agent uses it to generate tool definitions for the LLM:
// product_get(id) → calls same IEntityToolGenerator
```

This ensures parity: the tools an agent sees are identical to the tools an MCP client sees. No divergence.

### Part 8: Usage Examples

**Customer support agent:**

```csharp
var supportAgent = Agent.Create()
    .System(await Prompt.Load("support-agent"))
    .WithEntities<Customer, Order, Product>(write: false)
    .WithSearch<KnowledgeArticle>()
    .WithMemory(AgentMemory.Entity<ConversationLog>())
    .WithMaxIterations(8);

var result = await supportAgent.Run(
    "Customer #1234 says their order hasn't arrived",
    context: new { SessionId = sessionId });

// Agent autonomously:
// 1. Looks up Customer #1234
// 2. Queries recent Orders for that customer
// 3. Finds the pending order
// 4. Searches KnowledgeArticle for shipping delay policy
// 5. Composes response with order status + policy guidance
```

**Data analysis agent with code execution:**

```csharp
var analyst = Agent.Create()
    .System("You are a data analyst. Use tools to query data and Python to analyze it.")
    .WithEntities<SalesData, Product>(write: false)
    .WithTools(Tool.CodeExecution(new CodeSandbox
    {
        Language = "python",
        Packages = ["pandas", "matplotlib"],
        Timeout = TimeSpan.FromMinutes(2)
    }))
    .WithMaxIterations(15);

var result = await analyst.Run("What were our top 5 products by revenue last quarter?");
// Agent: queries SalesData, writes Python to aggregate, returns analysis
```

**Read-write agent (explicit opt-in):**

```csharp
var inventoryAgent = Agent.Create()
    .System("You manage inventory. You can update stock levels and prices.")
    .WithEntities<Product>(write: true)  // Explicit write access
    .WithMaxIterations(5);

var result = await inventoryAgent.Run("Set all Widget prices to $29.99");
// Agent: queries Products where Name contains "Widget", updates each Price, saves
// write: true required — without it, save/create/delete tools are not generated
```

### Part 9: What Agent Does NOT Do

Explicitly out of scope — these are orchestration concerns handled by external engines via MCP:

| Capability | Why Not | Alternative |
|-----------|---------|-------------|
| **Multi-agent orchestration** | Supervisor/swarm patterns are engine concerns | LangGraph/CrewAI via MCP |
| **Durable workflow persistence** | State machine infrastructure is massive | Temporal/Prefect via MCP |
| **Human-in-the-loop with async resume** | Requires durable execution and queue management | External workflow + `Review.*` (AI-0030) |
| **Scheduled/triggered execution** | Job scheduling is infrastructure | Cron/Hangfire + `Agent.Run()` |
| **Agent-to-agent communication** | Message passing between agents is orchestration | External supervisor via MCP |

The agent is a **single-turn reasoning loop**. For multi-step workflows with persistence, use external engines that consume Koan via MCP. For human review of agent outputs, use `Review.*` (AI-0030).

### Part 10: Package and Dependencies

**Package:** `Koan.AI.Agents` (follows Reference = Intent — adding the package reference enables Agent.*)

**Dependencies:**
- `Koan.AI` — Client.Chat() with tool calling (AI-0021)
- `Koan.AI.Contracts` — Tool definitions, shared models
- `Koan.AI.Orchestration` — Chain infrastructure (AI-0026), reused for tool execution
- `Koan.AI.Prompt` — Prompt type (AI-0025), optional

**Internal dependency on MCP Code Mode reflection:**
- `IEntityToolGenerator` shared with `Koan.Mcp` (AI-0014) — not a package dependency, shared via `Koan.AI.Contracts`

## Consequences

### Positive

- **Unique differentiator.** No other framework auto-generates typed tools from a data model. Entity-aware agents are a Koan-exclusive capability.
- **Minimal new code.** Agent reuses Chain infrastructure (AI-0026), Client tool calling (AI-0021), entity reflection (AI-0014), and entity memory. The new code is primarily the ReAct loop and tool generation facade — estimated ~2,000-3,000 lines.
- **MCP parity.** Agent tools and MCP tools use the same generator. No divergence between what an agent sees and what an MCP client sees.
- **Read-only by default.** Mutation requires explicit `write: true`, preventing accidental data modification by autonomous agents.
- **Progressive disclosure.** One-liner (`Agent.Create().WithEntities<T>().Run(goal)`) for simple cases. Full control (custom tools, memory, planning strategy, budgets) for complex cases.
- **Memory as entities.** Conversation history is queryable, deletable, and auditable via standard entity operations. No separate memory store.

### Negative / Trade-offs

- **Not a full agent framework.** No multi-agent, no durable workflows, no HITL with async resume. Teams needing these must use external engines.
- **Tool calling model dependency.** ReAct/FunctionCalling strategies work best with models that support native tool calling. Fallback to prompt-based invocation works but is less reliable.
- **Entity tool explosion.** `.WithEntities<Product, Order, Customer, Invoice, LineItem>()` generates 15+ tools. Models have limited tool context windows. Recommend limiting to 3-5 entity types per agent.
- **Write operations are risky.** Even with `write: true` opt-in, an autonomous agent modifying production data requires careful guardrails. The agent may misunderstand the goal and modify incorrectly. Recommend using write agents only with human review of the execution trace.

## References

- AI-0022: Unified AI Lifecycle Vision (parent, build-vs-interop decision)
- AI-0026: Chain Composition (infrastructure reused by Agent)
- AI-0025: Prompt Primitive (named prompts for agent system prompts)
- AI-0021: Category-Driven AI (Client.Chat with tool calling)
- AI-0014: MCP Code Mode (entity introspection, IEntityToolGenerator shared)
- AI-0012: MCP JSON-RPC Runtime (interop layer for external engines)
- AI-0030: Review Queues (human review of agent outputs)
- `src/Koan.AI/Client.cs` — Tool calling infrastructure
- `src/Koan.Mcp/` — Entity reflection for tool generation