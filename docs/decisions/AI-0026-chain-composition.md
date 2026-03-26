---
id: AI-0026
slug: AI-0026-chain-composition
domain: AI
status: Accepted
date: 2026-03-20
implementation: "Implemented in src/Koan.AI.Orchestration/ with Chain.* facade"
---

# ADR: Chain Composition — Typed, Immutable AI Pipeline Primitives

**Contract**

- **Inputs:** Variable dictionaries (anonymous objects, dictionaries) passed to `.Run()` / `.Stream()`, entity types for retrieval via `Vector<T>.Search()`, prompt templates (inline strings or `PromptEntry` via AI-0025), tool definitions from entity CRUD or service methods, chain memory configuration, AI category scope overrides (AI-0021).
- **Outputs:** `ChainResult` carrying generated text, typed parsed output via `Parsed<T>()`, RAG citations with source and relevance, execution metrics (tokens, latency, step timings); `IAsyncEnumerable<ChainChunk>` for streaming; HTTP endpoint responses via `app.MapChain()`.
- **Error Modes:** Retrieval step returns zero documents: chain continues with empty `{context}`, LLM receives the prompt without context, result includes diagnostic in `ChainResult.Metrics.Warnings`; `.Parse<T>()` receives malformed LLM output: retry once with stricter constraint prompt, then throw `ChainParseException` with raw text and target type; `.Branch()` receives classification result matching no branch: throw `ChainRoutingException` listing expected categories and actual classification; chain step timeout: individual step fails with `ChainStepTimeoutException`, chain aborts; AI provider unavailable mid-chain: circuit breaker fires per AI-0009, chain fails with `ChainExecutionException` wrapping provider error; `.Moderate()` flags content: chain halts, returns `ChainResult` with `Moderated = true` and empty text.
- **Acceptance Criteria:** A developer can build a RAG chain with `Chain.Create().Retrieve<T>().Chat()` and execute it with `.Run()` in under 5 lines; chains are immutable — calling `.Chat()` on a builder returns a new builder, the original is unchanged; `.Parse<T>()` sends JSON schema as a constraint to the LLM and deserializes the response; `.Branch()` routes to different sub-chains based on `.Classify()` output; `.Parallel()` fans out to multiple chains and merges results into the variable context; `app.MapChain()` exposes a chain as a POST endpoint accepting a JSON body mapped to variables; chains compose with existing augmentations (AI-0010) — augmentations fire within each `Client.Chat()` call inside the chain; `.Stream()` emits tokens as they arrive from the final `.Chat()` step.

**Edge Cases**

- Chain with `.Retrieve<T>()` but no vectors stored for `T`: Retrieval returns empty list, `{context}` resolves to empty string, LLM generates without context. `ChainResult.Metrics.Warnings` includes "Retrieve<{Type}> returned 0 results". No exception — the chain completes.
- `.Parse<T>()` after `.Stream()`: Not supported. `.Stream()` returns raw chunks. To get parsed output, use `.Run()`. Calling `.Stream()` on a chain ending with `.Parse<T>()` throws `InvalidOperationException` at build time (when `.Stream()` is called), not at runtime.
- `.Branch()` with overlapping category names: Last branch wins. Builder logs a warning during construction.
- `.Parallel()` with a failing sub-chain: The parallel step collects all results. Failed sub-chains produce `null` in their named slot and add a diagnostic to `ChainResult.Metrics.Warnings`. The chain continues — downstream templates referencing the failed slot resolve to empty string.
- `.WithMemory()` on a chain served via `app.MapChain()`: Memory is keyed by a `session_id` field in the request body. Missing `session_id` creates a new ephemeral session per request.
- Chain with no `.Chat()` step (only `.Retrieve<T>()`): Valid. Returns retrieval results as text in `ChainResult.Text`. Useful for retrieval-only pipelines.
- `.Moderate()` on streaming chain: Moderation runs on accumulated text at configurable intervals (default: every 50 tokens). If flagged mid-stream, stream emits a final `ChainChunk` with `Moderated = true` and terminates.
- Variable referenced in template but not provided in `.Run()`: Throws `ChainVariableException` listing missing variables. Checked eagerly before first step executes.
- `.Scope()` inside a chain vs outside: `.Scope()` on the chain builder applies to all LLM calls within that chain. `Client.Scope()` wrapping `.Run()` applies as ambient scope but chain-level `.Scope()` takes precedence.

## Context

Koan.AI provides a mature inference surface through `Client.Chat()`, `Client.Embed()`, `Client.Stream()`, and `Client.Scope()` (AI-0021), with entity-first embedding via `[Embedding]` (AI-0020) and a modular augmentation pipeline (AI-0010). These primitives handle single-turn interactions well. However, real-world AI features rarely consist of a single LLM call.

Production applications built on Koan exhibit recurring multi-step patterns:

1. **RAG pipelines are hand-rolled.** S6.SnapVault's `PhotoProcessingService` manually retrieves vectors, formats context, calls `Client.Chat()`, and parses the response. S7.Meridian's `SchemaGuidedExtractor` does the same with a different retrieval strategy. The structure is identical; the implementation is duplicated.

2. **Retrieval-then-generate requires boilerplate.** A developer must call `Vector<T>.Search()`, format results into a string, construct a prompt with the context, call `Client.Chat()`, and optionally parse the output. Five steps that could be two: retrieve and chat.

3. **Conditional routing is imperative.** A support chatbot that routes technical questions to one model and billing questions to another requires manual classification, a switch statement, and duplicated call infrastructure. This is a composition problem, not a business logic problem.

4. **Structured output is fragile.** Extracting typed objects from LLM responses requires manual JSON parsing, retry logic for malformed output, and schema communication to the LLM. This is mechanical work the framework should own.

5. **No reusable pipeline definition.** Each multi-step AI workflow is an imperative method. There is no way to inspect, test, serialize, or share the pipeline structure. A chain defined in code cannot be served as an endpoint without writing a controller.

LangChain's LCEL (LangChain Expression Language) demonstrated that composition primitives dramatically reduce boilerplate for AI pipelines. However, LangChain's approach has weaknesses in the Koan context: it is dynamically typed (errors surface at runtime), storage-agnostic (requires separate vector store configuration), and Python-first. Koan can do better by building composition primitives that are type-safe, entity-aware, and native to the existing AI infrastructure.

The unified AI lifecycle vision (AI-0022) identifies `Chain.*` as Phase 4, dependent on `Prompt()` (AI-0025) for named prompt support and building on `Client.*` (AI-0021) for inference routing. This ADR specifies the composition primitives.

### Design Constraints

- **Chains must compose with augmentations, not replace them.** The augmentation pipeline (AI-0010) handles cross-cutting concerns (moderation, budgeting, redaction, history). Chains orchestrate multi-step flows. Each `Client.Chat()` call inside a chain fires augmentations normally. Chains do not bypass or duplicate augmentation functionality.
- **Chains must not introduce a second retrieval path.** `Vector<T>.Search()` is the retrieval primitive. `.Retrieve<T>()` in a chain delegates to it. No separate vector store abstraction.
- **Chains must not introduce agent semantics.** Autonomous decision-making, tool-use loops, and stateful multi-actor workflows are deferred to future ADRs (AI-0022, Part 11). Chains are deterministic pipelines with conditional branching — not agents.

## Decision

### Part 1: Core Abstraction — Chains Are Immutable Blueprints

A chain is a **description** of an AI pipeline, not an execution. Calling `.Run()` creates an execution from the blueprint. This separation enables:

- **Reuse:** One chain definition serves many requests.
- **Testing:** Chains can be inspected, their steps enumerated, without executing.
- **Serialization:** Chain structure can be persisted and reconstructed.
- **Endpoint binding:** `app.MapChain()` binds a chain to HTTP without custom controllers.

The builder pattern produces immutable step lists. Each builder method returns a **new** `ChainBuilder` instance — the original is never mutated:

```csharp
var baseChain = Chain.Create()
    .System("You are a helpful assistant.");

// baseChain is unchanged — withRetrieval is a new builder
var withRetrieval = baseChain
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5);

// Both are independently valid chains
var simpleAnswer = baseChain.Chat("{question}");
var ragAnswer = withRetrieval.Chat("{question}\n\nContext:\n{context}");
```

### Part 2: Chain Entry Point and Builder

```csharp
/// <summary>
/// Entry point for chain construction. Chains are immutable blueprints
/// that describe AI pipelines. Call .Run() or .Stream() to execute.
/// </summary>
public sealed class Chain
{
    /// <summary>Start a new chain builder.</summary>
    public static ChainBuilder Create();
}
```

`ChainBuilder` is the fluent API. Each method appends a step to the immutable step list and returns a new builder:

```csharp
public sealed class ChainBuilder
{
    // ── Prompt Configuration ─────────────────────────────────────

    /// <summary>Set the system prompt for subsequent Chat steps.</summary>
    public ChainBuilder System(string systemPrompt);

    /// <summary>Set the system prompt from a Prompt value (AI-0025).</summary>
    public ChainBuilder System(Prompt prompt);

    /// <summary>Load a named prompt from the PromptEntry catalog (AI-0025).</summary>
    public ChainBuilder WithPrompt(string promptName);

    // ── Generation ───────────────────────────────────────────────

    /// <summary>
    /// LLM generation step. Template variables like {question} resolve
    /// from the execution context (Run variables + prior step outputs).
    /// </summary>
    public ChainBuilder Chat(string template);

    // ── Retrieval ────────────────────────────────────────────────

    /// <summary>
    /// Entity-aware vector retrieval. Delegates to Vector&lt;T&gt;.Search().
    /// Results populate {context} in the variable context.
    /// </summary>
    /// <param name="query">Query template with variable interpolation.</param>
    /// <param name="topK">Maximum documents to retrieve.</param>
    /// <param name="alpha">Hybrid search weight (0=keyword, 1=semantic).</param>
    /// <param name="rerank">Enable cross-encoder re-ranking.</param>
    public ChainBuilder Retrieve<T>(
        string query,
        int topK = 5,
        double alpha = 0.5,
        bool rerank = false) where T : class, IEntity;

    // ── Structured Output ────────────────────────────────────────

    /// <summary>
    /// Parse the previous Chat step's output into a typed object.
    /// Sends JSON schema as a constraint to the LLM. Retries once
    /// on parse failure with a stricter prompt.
    /// </summary>
    public ChainBuilder Parse<T>();

    // ── Classification and Routing ───────────────────────────────

    /// <summary>
    /// Classify input into one of the provided categories.
    /// Result is available as {classification} in the variable context.
    /// </summary>
    public ChainBuilder Classify(string input, string[] categories);

    /// <summary>
    /// Route to different sub-chains based on classification result
    /// or a predicate evaluated against the variable context.
    /// </summary>
    public ChainBuilder Branch(params (string category, ChainBuilder chain)[] branches);

    // ── Fan-out ──────────────────────────────────────────────────

    /// <summary>
    /// Execute multiple chains in parallel. Each chain's result is
    /// added to the variable context under its name.
    /// </summary>
    public ChainBuilder Parallel(params (string name, ChainBuilder chain)[] chains);

    // ── Post-Retrieval Processing ────────────────────────────────

    /// <summary>Cross-encoder re-ranking of retrieved documents.</summary>
    public ChainBuilder Rerank();

    /// <summary>
    /// Contextual compression — extract only relevant portions
    /// from retrieved documents relative to the query.
    /// </summary>
    public ChainBuilder Compress();

    // ── Safety ───────────────────────────────────────────────────

    /// <summary>
    /// Content moderation guardrail. Checks the specified target
    /// (default: {input}) against moderation policy. Halts chain
    /// if content is flagged.
    /// </summary>
    public ChainBuilder Moderate(string target = "{input}");

    // ── Embedding ────────────────────────────────────────────────

    /// <summary>Generate an embedding mid-chain. Result stored as {embedding}.</summary>
    public ChainBuilder Embed(string text);

    // ── Tool Use ─────────────────────────────────────────────────

    /// <summary>
    /// Attach tool definitions for function calling within Chat steps.
    /// The LLM can invoke these tools; the chain executor handles
    /// the tool call loop.
    /// </summary>
    public ChainBuilder WithTools(params Tool[] tools);

    // ── Memory ───────────────────────────────────────────────────

    /// <summary>
    /// Add conversation memory to the chain. Memory is injected
    /// into Chat steps as prior conversation turns.
    /// </summary>
    public ChainBuilder WithMemory(ChainMemory memory);

    // ── Routing ──────────────────────────────────────────────────

    /// <summary>
    /// Route AI calls within this chain to specific sources.
    /// Overrides ambient Client.Scope() for calls made by this chain.
    /// </summary>
    public ChainBuilder Scope(string? chat = null, string? embed = null);

    // ── Execution ────────────────────────────────────────────────

    /// <summary>
    /// Execute the chain and return the result. Variables are resolved
    /// from the provided object's properties or dictionary entries.
    /// </summary>
    public Task<ChainResult> Run(
        object? variables = null,
        CancellationToken ct = default);

    /// <summary>
    /// Execute the chain with token-by-token streaming.
    /// Only the final Chat step streams; prior steps execute fully.
    /// </summary>
    public IAsyncEnumerable<ChainChunk> Stream(
        object? variables = null,
        CancellationToken ct = default);
}
```

### Part 3: Result Types

```csharp
/// <summary>
/// The result of a chain execution. Carries text output, optional
/// parsed object, citations from RAG steps, and execution metrics.
/// </summary>
public sealed record ChainResult
{
    /// <summary>Raw text output from the final generation step.</summary>
    public string Text { get; init; } = "";

    /// <summary>Whether moderation halted the chain.</summary>
    public bool Moderated { get; init; }

    /// <summary>
    /// Deserialize the text output into a typed object.
    /// Returns null if Parse&lt;T&gt;() was not used or parsing failed.
    /// </summary>
    public T? Parsed<T>();

    /// <summary>Citations from Retrieve steps, ordered by relevance.</summary>
    public IReadOnlyList<Citation>? Citations { get; init; }

    /// <summary>Execution metrics: tokens, latency, per-step timings.</summary>
    public ChainMetrics Metrics { get; init; }
}

/// <summary>A citation linking a generated claim to a retrieved source.</summary>
public sealed record Citation(
    string Source,
    string Excerpt,
    double Relevance);

/// <summary>A streaming chunk from a chain execution.</summary>
public sealed record ChainChunk
{
    /// <summary>Incremental text delta.</summary>
    public string Delta { get; init; } = "";

    /// <summary>True if this is the final chunk.</summary>
    public bool Done { get; init; }

    /// <summary>True if moderation halted the stream.</summary>
    public bool Moderated { get; init; }

    /// <summary>Cumulative token count (incremental).</summary>
    public int? TokensOut { get; init; }
}

/// <summary>Execution metrics for a completed chain.</summary>
public sealed record ChainMetrics
{
    /// <summary>Total input tokens across all LLM calls.</summary>
    public int TotalTokensIn { get; init; }

    /// <summary>Total output tokens across all LLM calls.</summary>
    public int TotalTokensOut { get; init; }

    /// <summary>Wall-clock duration of the entire chain.</summary>
    public TimeSpan TotalLatency { get; init; }

    /// <summary>Per-step timing and token breakdown.</summary>
    public IReadOnlyList<StepMetric> Steps { get; init; } = [];

    /// <summary>Diagnostic warnings (e.g., empty retrieval, failed parallel branches).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>Metrics for a single chain step.</summary>
public sealed record StepMetric(
    string StepName,
    TimeSpan Duration,
    int? TokensIn = null,
    int? TokensOut = null);
```

### Part 4: Memory Types

Chain memory provides conversation continuity across multiple `.Run()` invocations. Memory is injected into `.Chat()` steps as prior conversation turns.

```csharp
/// <summary>
/// Conversation memory for chains. Three strategies with different
/// trade-offs between fidelity and token cost.
/// </summary>
public abstract record ChainMemory
{
    /// <summary>
    /// Sliding window — keep the last N turns verbatim.
    /// Simple, predictable token cost, loses old context.
    /// </summary>
    public static ChainMemory Sliding(int maxTurns = 10);

    /// <summary>
    /// Summary — compress older turns into a running summary.
    /// Preserves key facts, costs one summarization call per overflow.
    /// </summary>
    public static ChainMemory Summary();

    /// <summary>
    /// Entity-backed — persist conversation history as entities.
    /// Survives restarts. Query-able. Integrates with entity lifecycle.
    /// </summary>
    public static ChainMemory Entity<T>() where T : Entity<T>;
}
```

**Memory keying:** When a chain uses memory, each execution is associated with a session. For programmatic use, the session ID is passed via variables (`new { session_id = "user-123" }`). For `app.MapChain()` endpoints, the `session_id` field in the request body is used. If no session ID is provided, each `.Run()` call creates an ephemeral session (no memory across calls).

### Part 5: Tool Definitions

Tools enable function calling within `.Chat()` steps. The LLM decides when to invoke a tool; the chain executor handles the call and feeds the result back.

```csharp
/// <summary>
/// Tool definitions for function calling in chain Chat steps.
/// Tools can be derived from service methods or entity CRUD operations.
/// </summary>
public sealed class Tool
{
    /// <summary>
    /// Create tools from service methods. Methods are discovered by name
    /// and their parameters become the tool's JSON schema.
    /// </summary>
    public static Tool From<TService>(params string[] methods);

    /// <summary>
    /// Create CRUD tools from an entity type. Generates Get, Query,
    /// Create, Update, Delete tools with schemas derived from the entity.
    /// </summary>
    public static Tool FromEntity<T>(params string[] operations) where T : Entity<T>;
}
```

```csharp
// Entity-derived tools — CRUD auto-generated from entity schema
var tools = Tool.FromEntity<Product>("Get", "Query");

var assistant = Chain.Create()
    .System("You are a product assistant. Use tools to look up products.")
    .WithTools(tools)
    .Chat("{question}");

// Service-derived tools
var weatherTool = Tool.From<IWeatherService>("GetForecast", "GetCurrent");

var agent = Chain.Create()
    .System("Help users with weather questions.")
    .WithTools(weatherTool)
    .Chat("{question}");
```

**Tool call loop:** When the LLM returns a tool call instead of text, the chain executor invokes the tool, appends the result as a tool message, and calls the LLM again. This loop continues until the LLM produces a text response or a configurable maximum iteration count is reached (default: 10).

### Part 6: Variable Resolution

Variables flow through the chain as an immutable dictionary that grows as steps execute:

1. **Initial variables** come from the object passed to `.Run()` or `.Stream()`. Properties are reflected into key-value pairs.
2. **Step outputs** are added to the context under well-known keys:
   - `.Retrieve<T>()` → `{context}` (formatted text of retrieved documents)
   - `.Classify()` → `{classification}` (the selected category string)
   - `.Parallel()` → `{name}` for each named sub-chain (the sub-chain's text output)
   - `.Embed()` → `{embedding}` (the vector, for downstream steps)
   - `.Chat()` → `{response}` (overwrites on each Chat step)
3. **Template interpolation** uses `{variableName}` syntax. Variables are resolved eagerly before each step. Missing variables throw `ChainVariableException` with a list of unresolved names.

```csharp
var chain = Chain.Create()
    .Retrieve<Document>(query: "{question}", topK: 5)  // {context} populated
    .System("Answer from context only.")
    .Chat("{question}\n\nContext:\n{context}");         // Both resolved

var result = await chain.Run(new { question = "What is the return policy?" });
// {question} = "What is the return policy?"
// {context}  = formatted text of 5 retrieved Document entities
```

**Entity text formatting:** Retrieved entities are formatted for LLM consumption. If the entity implements `IEmbeddingTextProvider` (provides `ToEmbeddingText()`), that method is used. Otherwise, public string properties are concatenated as `"PropertyName: Value\n"` pairs — the same convention as AI-0021's Chat entity context resolution.

### Part 7: Retrieval — Entity-Aware by Default

`.Retrieve<T>()` is the bridge between chains and Koan's entity-native vector search. It delegates to `Vector<T>.Search()` internally, inheriting all existing vector infrastructure:

```csharp
// What the developer writes:
Chain.Create()
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5, alpha: 0.7);

// What executes internally (simplified):
var results = await Vector<KnowledgeArticle>.Search(
    query: resolvedQuestion,
    topK: 5,
    alpha: 0.7);

// Results are:
// 1. Formatted as text and stored in {context}
// 2. Stored as Citation records for ChainResult.Citations
```

**Reranking and compression** are post-retrieval processing steps that refine the initial retrieval:

```csharp
var advancedRag = Chain.Create()
    .Retrieve<Document>(query: "{question}", topK: 20)
    .Rerank()       // Cross-encoder scores all 20, keeps top 5
    .Compress()     // Extracts only question-relevant portions
    .System("Answer using only the provided context. Cite sources.")
    .Chat("{question}\n\nContext:\n{context}");
```

- **`.Rerank()`** uses a cross-encoder model (configured via `Koan:Ai:Chain:RerankModel` or auto-resolved) to re-score retrieved documents against the query. Default: keep top 5 after reranking. The cross-encoder provides more accurate relevance scores than the initial bi-encoder retrieval.
- **`.Compress()`** uses an LLM call to extract only the portions of each document that are relevant to the query. This reduces token usage in the subsequent `.Chat()` step and improves answer quality by removing noise.

Both steps update `{context}` in-place. Citations are updated to reflect reranked scores and compressed excerpts.

### Part 8: Structured Output with Parse

`.Parse<T>()` enforces typed output from LLM generation:

```csharp
public sealed record SupportResponse
{
    public required string Answer { get; init; }
    public required string Confidence { get; init; }  // "high", "medium", "low"
    public string? Escalation { get; init; }
}

var chain = Chain.Create()
    .System("You are a support agent. Respond in the required JSON format.")
    .Chat("{question}")
    .Parse<SupportResponse>();

var result = await chain.Run(new { question = "How do I reset my password?" });
SupportResponse response = result.Parsed<SupportResponse>()!;
// response.Answer = "To reset your password, go to Settings > Security > ..."
// response.Confidence = "high"
// response.Escalation = null
```

**How it works:**

1. The JSON schema for `T` is derived at build time (via `System.Text.Json` schema generation or source generator).
2. The schema is appended to the `.Chat()` prompt as a constraint: `"Respond with valid JSON matching this schema: {...}"`.
3. The LLM response is deserialized into `T`.
4. On deserialization failure, the chain retries **once** with a stricter prompt that includes the failed output and the error message.
5. On second failure, `ChainParseException` is thrown with the raw LLM text and the target type.

The parsed object is accessible via `ChainResult.Parsed<T>()`. The raw text remains in `ChainResult.Text`.

### Part 9: Classification and Branching

`.Classify()` and `.Branch()` enable conditional routing within a chain:

```csharp
var router = Chain.Create()
    .Classify("{input}", categories: ["technical", "billing", "general"])
    .Branch(
        ("technical", Chain.Create()
            .Scope(chat: "ollama-code")
            .Retrieve<TechDoc>(query: "{input}", topK: 3)
            .System("You are a technical support specialist.")
            .Chat("{input}\n\nDocumentation:\n{context}")),
        ("billing", Chain.Create()
            .Retrieve<BillingPolicy>(query: "{input}", topK: 2)
            .System("You are a billing support agent. Reference policy numbers.")
            .Chat("{input}\n\nPolicies:\n{context}")),
        ("general", Chain.Create()
            .System("You are a friendly support agent.")
            .Chat("{input}")));

var result = await router.Run(new { input = "My GPU keeps crashing during training" });
// → Classified as "technical"
// → Routed to technical branch with TechDoc retrieval and ollama-code model
```

**Classification implementation:** `.Classify()` issues an LLM call with a constrained prompt: `"Classify the following into exactly one category: [technical, billing, general]. Respond with the category name only."` The response is normalized (trimmed, lowercased) and matched against the provided categories.

**Branch resolution:** The classification result is matched against branch keys. If no match is found, `ChainRoutingException` is thrown with the expected categories and the actual classification result. A default/fallback branch can be specified with the key `"*"`:

```csharp
.Branch(
    ("technical", technicalChain),
    ("billing",   billingChain),
    ("*",         fallbackChain))    // Catches unmatched classifications
```

### Part 10: Parallel Execution

`.Parallel()` fans out to multiple chains that execute concurrently:

```csharp
var legalResearch = Chain.Create()
    .Parallel(
        ("caseLaw", Chain.Create()
            .Retrieve<CaseLaw>(query: "{question}", topK: 10)
            .Rerank()),
        ("statutes", Chain.Create()
            .Retrieve<Statute>(query: "{question}", topK: 5)),
        ("commentary", Chain.Create()
            .Retrieve<LegalCommentary>(query: "{question}", topK: 3)))
    .System("You are a legal research assistant. Synthesize findings from all sources. Cite specific cases and statutes.")
    .Chat("{question}\n\nCase Law:\n{caseLaw}\n\nStatutes:\n{statutes}\n\nCommentary:\n{commentary}");
```

**Execution semantics:**

- All sub-chains execute concurrently via `Task.WhenAll`.
- Each sub-chain's `ChainResult.Text` is stored in the variable context under its name.
- If a sub-chain fails, its named slot is set to empty string. The failure is recorded in `ChainResult.Metrics.Warnings`. The parent chain continues.
- Citations from all sub-chains are merged into the parent's `ChainResult.Citations`.
- Cancellation of the parent chain cancels all sub-chains.

### Part 11: Integration with Augmentation Pipeline (AI-0010)

Chains and augmentations operate at **different levels of abstraction**. Augmentations are cross-cutting concerns that apply to individual LLM calls. Chains orchestrate sequences of LLM calls. They compose naturally:

```
Chain.Run()
  ├── Step 1: Retrieve<T>()          → Vector<T>.Search() [no augmentation]
  ├── Step 2: Rerank()               → Cross-encoder call [no augmentation]
  ├── Step 3: Chat("{question}...")   → Client.Chat() internally
  │     ├── IAugmentation.OnPrepare  → SystemPromptAugmentation adds policy
  │     ├── IAugmentation.OnBefore   → BudgetGuardAugmentation checks quota
  │     ├── [LLM call]
  │     ├── IAugmentation.OnAfter    → RedactionAugmentation scrubs PII
  │     └── IAugmentation.OnAfter    → ModerationAugmentation checks output
  └── Step 4: Parse<T>()            → Deserialization [no augmentation]
```

**Key design points:**

- **No duplication.** Chain-level `.Moderate()` is a chain step (checks text between steps). Augmentation-level `ModerationAugmentation` runs within each `Client.Chat()` call. They serve different purposes: chain-level moderation checks intermediate values (e.g., user input before it reaches the LLM); augmentation-level moderation checks LLM output.
- **No bypass.** Chains call `Client.Chat()` / `Client.Embed()` through the normal path. Augmentations fire. There is no "raw" LLM call mode in chains.
- **RAG context isolation.** Chain `.Retrieve<T>()` populates `{context}` in the chain variable context. This is separate from `RagAugmentation` (AI-0010), which retrieves and injects context at the augmentation level. If both are active, both contexts are included. Recommendation: use chain-level retrieval OR augmentation-level RAG, not both on the same chain. The framework logs a warning if both are detected.
- **Budget enforcement.** `BudgetGuardAugmentation` tracks tokens per `Client.Chat()` call. Chain-level token tracking in `ChainMetrics` aggregates across all steps. Both work independently. A budget exceeded mid-chain aborts the chain with `ChainExecutionException`.

### Part 12: HTTP Endpoint Binding

`app.MapChain()` wires a chain to an HTTP endpoint with zero controller code:

```csharp
var rag = Chain.Create()
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5)
    .System("Answer from context only. Cite sources.")
    .Chat("{question}\n\nContext:\n{context}")
    .Parse<AnswerWithCitations>();

// POST /api/ask with body { "question": "..." }
app.MapChain("/api/ask", rag);

// With streaming: POST /api/ask/stream returns SSE
app.MapChain("/api/ask", rag, streaming: true);
```

**Request mapping:**

- The HTTP request body (JSON) is deserialized into a `Dictionary<string, object>` and passed as the chain's variables.
- For non-streaming: returns `ChainResult` serialized as JSON.
- For streaming: returns `text/event-stream` with `ChainChunk` events.
- `session_id` in the request body is used for memory keying if the chain has `.WithMemory()`.

**Response shape (non-streaming):**

```json
{
  "text": "The refund policy allows returns within 30 days...",
  "parsed": {
    "answer": "The refund policy allows returns within 30 days...",
    "sources": ["KB-1234", "KB-5678"]
  },
  "citations": [
    { "source": "KB-1234", "excerpt": "Returns accepted within 30 days...", "relevance": 0.94 },
    { "source": "KB-5678", "excerpt": "Full refund for unused items...", "relevance": 0.87 }
  ],
  "metrics": {
    "totalTokensIn": 1250,
    "totalTokensOut": 180,
    "totalLatencyMs": 2340
  }
}
```

### Part 13: Prompt Integration (AI-0025)

Chains integrate with the `Prompt` type and `PromptEntry` catalog:

```csharp
// Inline prompt (simple)
var chain = Chain.Create()
    .System("You are a {role}.")
    .Chat("{question}");

// Prompt value (structured, inspectable)
var systemPrompt = Prompt(p => p
    .System("You are a {role}")
    .Constrain("Max 3 sentences")
    .OutputAs<Summary>());

var chain = Chain.Create()
    .System(systemPrompt)
    .Chat("{question}")
    .Parse<Summary>();

// Named prompt from catalog (versionable, editable by domain experts)
var chain = Chain.Create()
    .WithPrompt("support-response")   // Loads PromptEntry by name
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5)
    .Chat("{question}\n\nContext:\n{context}");
```

`.WithPrompt()` loads a `PromptEntry` entity at execution time (not at build time), so prompt changes take effect without redeploying code. The loaded prompt's system message, constraints, and output format are applied to subsequent `.Chat()` steps.

### Part 14: Scope Integration (AI-0021)

Chain-level `.Scope()` routes AI calls within the chain to specific sources, integrating with AI-0021's category-driven routing:

```csharp
// Route all chain LLM calls to specific sources
var chain = Chain.Create()
    .Scope(chat: "openai-gpt4", embed: "local-embed")
    .Retrieve<Document>(query: "{question}", topK: 5)  // Embed uses local-embed
    .Chat("{question}\n\nContext:\n{context}");          // Chat uses openai-gpt4

// Per-branch scoping for cost optimization
var router = Chain.Create()
    .Classify("{input}", categories: ["simple", "complex"])
    .Branch(
        ("simple",  Chain.Create()
            .Scope(chat: "ollama-small")
            .Chat("{input}")),
        ("complex", Chain.Create()
            .Scope(chat: "openai-gpt4")
            .Retrieve<Document>(query: "{input}", topK: 10)
            .Chat("{input}\n\nContext:\n{context}")));
```

**Precedence order** (highest to lowest):

1. Chain-level `.Scope()` on the innermost chain (e.g., branch sub-chain)
2. Chain-level `.Scope()` on the parent chain
3. Ambient `Client.Scope()` wrapping `.Run()`
4. Category defaults from `Koan:Ai:{Category}:Source` configuration
5. Auto-discovered source

## Consequences

### Positive

- **Entity-aware retrieval eliminates boilerplate.** `.Retrieve<KnowledgeArticle>(...)` replaces 10+ lines of manual `Vector<T>.Search()`, formatting, and prompt assembly. The entity type carries its vector configuration — no separate vector store setup.
- **Type safety catches errors at compile time.** `.Retrieve<T>()` constrains `T` to `IEntity`. `.Parse<T>()` generates a schema from the type. Template variable typos surface as `ChainVariableException` before the first LLM call, not as silent empty strings.
- **Immutable blueprints enable reuse and testing.** A chain can be defined once, tested with mock variables, inspected for step structure, and served via HTTP — all without mutation or side effects.
- **Composition replaces imperative orchestration.** Branching, parallel execution, and sequential chaining are declarative. The pipeline structure is visible in the code, not buried in if-else blocks.
- **Augmentations compose naturally.** Chains do not bypass or duplicate the augmentation pipeline. Cross-cutting concerns (moderation, budgeting, redaction) continue to work at the per-call level. Chain-level moderation adds pipeline-level guardrails on top.
- **Progressive disclosure maintained.** A simple RAG chain is 4 lines. Advanced features (reranking, compression, branching, memory, tools) are additive — they do not increase the complexity of simple chains.
- **HTTP binding reduces boilerplate.** `app.MapChain()` eliminates the need for custom controllers, request DTOs, and response mapping for AI endpoints. One line to expose a chain as an API.

### Negative / Trade-offs

- **New abstraction layer.** Chains add a concept between `Client.Chat()` and application code. Developers must learn when to use chains vs direct `Client` calls. Guidance: use `Client` for single-turn interactions; use chains when you have retrieval, parsing, branching, or multi-step flows.
- **Streaming is limited to the final Chat step.** Intermediate steps (retrieval, classification, reranking) execute fully before streaming begins. This means time-to-first-token includes all prior steps. For chains with expensive retrieval, this may be noticeable.
- **Variable resolution is string-based.** Template variables (`{question}`, `{context}`) are resolved by name at runtime, not at compile time. Typos in variable names surface as runtime exceptions, not compile errors. Mitigated by eager validation before the first step executes.
- **`.Parse<T>()` reliability depends on LLM capability.** Smaller models may struggle with complex output schemas. The single-retry strategy helps but does not guarantee success. Developers should keep parse targets simple (flat records with primitive properties) when using smaller models.
- **Agent semantics explicitly excluded.** Chains do not support autonomous tool-use loops, goal-driven behavior, or multi-actor workflows. Teams needing these capabilities must wait for future ADRs or use external orchestration (LangGraph). This is a deliberate scope constraint, not a gap — the principle is to build composition primitives before agent abstractions.
- **`.Parallel()` failure semantics are lenient.** Failed sub-chains produce empty slots rather than failing the parent chain. This prevents cascading failures but may produce low-quality results if a critical sub-chain fails silently. Developers must check `ChainResult.Metrics.Warnings` or use strict mode (future enhancement) if all branches are required.

### Package

`Koan.AI.Orchestration` — contains `Chain`, `ChainBuilder`, `ChainResult`, `ChainChunk`, `ChainMetrics`, `ChainMemory`, `Tool`, `Citation`, and all chain step implementations. Depends on `Koan.AI` (Client facade), `Koan.AI.Contracts` (adapter interfaces), `Koan.AI.Prompt` (Prompt type and PromptEntry), and `Koan.Data.AI` (Vector<T> search).

## References

- AI-0010: Prompt Entrypoint and Augmentation Pipeline (augmentation lifecycle, cross-cutting concerns)
- AI-0021: Category-Driven AI with Convention Defaults (Client facade, category routing, Scope)
- AI-0022: Unified AI Lifecycle Vision (Chain as Part 6, package structure, persona alignment, phasing)
- AI-0025: Prompt Type and PromptEntry Catalog (Prompt values, named prompts, PromptEntry entity)
- AI-0009: Multi-Service Routing and Policies (circuit breaker, fallback, health monitoring)
- AI-0020: Entity-First AI and Transaction Coordination (`[Embedding]` lifecycle, `Vector<T>`)
- ADR-0051: Vector Hybrid Search (`Vector<T>.Search()`, alpha parameter, hybrid retrieval)
- `src/Koan.AI/Client.cs` — Static facade used internally by chain steps
- `src/Koan.AI/Pipeline/AiRoutingEngine.cs` — Routing engine used by chain scope resolution
- `src/Koan.Data.AI/EntityEmbeddingExtensions.cs` — `Vector<T>.Search()` used by `.Retrieve<T>()`
- `samples/S6.SnapVault/` — Reference for patterns chains replace (manual RAG orchestration)
- `samples/S7.Meridian/` — Reference for patterns chains replace (structured extraction)
