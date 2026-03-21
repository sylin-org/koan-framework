---
id: AI-0025
slug: AI-0025-prompt-primitive
domain: AI
status: Proposed
date: 2026-03-20
---

# ADR: Uri-Inspired Prompt Primitive

**Contract**

- **Inputs:** Raw prompt strings, structured prompt definitions via `PromptBuilder`, persisted `PromptEntry` entities from the data layer, variable context objects (anonymous types, entities, dictionaries), JSON schema types for output format specification.
- **Outputs:** Immutable `Prompt` value objects with parsed structure (variables, system directive, constraints, examples, output format, metadata); resolved prompt strings with variables substituted; `PromptEntry` entities with versioning, status lifecycle, and A/B test strategy; token estimates per model; unresolved variable diagnostics.
- **Error Modes:** `Prompt.Parse()` on null/empty string returns a `Prompt` with empty `Raw` and no variables (not an exception). `Prompt.Load()` with no matching `PromptEntry` falls back to a `PromptNotFoundException` with dev-mode guidance ("No prompt named '{name}' found. Create one via PromptEntry or use an inline Prompt()"). `Resolve()` with missing variables returns the raw template with `{variable}` placeholders intact and logs a warning listing unresolved names. `OutputAs<T>()` on a type with no public properties yields an empty JSON schema with a build-time analyzer warning. `Load()` with `PromptStrategy.ABTest` when fewer than two active versions exist degrades to `Latest` with a diagnostic log.
- **Acceptance Criteria:** A developer can `Prompt("Summarize {topic}")` and get an immutable object with `Variables = ["topic"]`; can `Prompt.Create(p => p.System("...").Instruct("...").Constrain("..."))` and get a structured prompt; can `await Prompt.Load("support-response")` and get a versioned prompt from the entity catalog; can `prompt.Resolve(new { topic = "AI" })` and get a fully substituted string; can pass a `Prompt` anywhere a `string` is expected via implicit conversion; can use `Prompt` directly with `Client.Chat()` and `Chain.*`; existing string-based `Client.Chat("text")` call sites continue to work unchanged.

**Edge Cases**

- Prompt string containing literal braces (e.g., JSON examples): `{variable}` is identified by regex `\{([a-zA-Z_][a-zA-Z0-9_]*)\}`; braces around non-identifier content (numbers, JSON, whitespace) are left untouched. For explicit literal braces around identifiers, use `{{escaped}}` which resolves to `{escaped}` without variable extraction.
- `Prompt.Load()` called before DI is configured (e.g., in a static initializer): Throws `InvalidOperationException` with message "Prompt.Load() requires a configured service provider. Use Prompt.Parse() or Prompt.Create() for static contexts."
- `Resolve()` with a typed entity context where property names don't match variable names: Unmatched variables remain as `{variable}` in output; `UnresolvedVariables(context)` returns the list for diagnostic inspection.
- `PromptEntry` with `Status = Retired` requested via `Prompt.Load("name")`: Skipped by default; `Prompt.Load("name", version: 3)` can explicitly load a retired version with a warning log.
- `Prompt.With()` called on a string-parsed prompt to add structure: Returns new `Prompt` with builder-derived parts merged; `Raw` updated to reflect the composed result.
- Concurrent `Prompt.Load()` calls for the same name: Entity query is deduplicated via the existing `SingleFlight` infrastructure (DATA-0057).
- Variable name collision between `Default()` values and `Resolve()` context: Explicit context wins; defaults fill only unresolved variables.
- `EstimateTokens()` with unknown model: Falls back to cl100k_base tokenizer (GPT-4 baseline) with a diagnostic noting the approximation.

## Context

Prompts in the Koan ecosystem are strings. `Client.Chat("Summarize this article")` takes a string. `ChatOptions.SystemPrompt` is a string. S6.SnapVault's `AnalysisPromptFactory` builds strings via `{{FOCUS_INSTRUCTIONS}}` replacement. S7.Meridian's `SourceTypeAuthoringService` and `AnalysisTypeAuthoringService` use `{{variable}}` substitution for schema-guided extraction prompts. Every application that uses AI builds its own ad-hoc prompt assembly layer.

This creates four problems:

1. **Prompts are opaque.** A 500-character prompt string reveals nothing about its structure — which parts are system instructions, which are user templates, which contain variables, what output format is expected. Debugging requires reading the entire string. Logging a prompt gives no insight into what will vary between calls.

2. **Variable substitution is inconsistent.** S6.SnapVault uses `{{double braces}}` (borrowed from Handlebars/Mustache convention). S7.Meridian uses the same convention. But C# string interpolation uses `{single braces}`, and .NET format strings use `{0}` or `{name}`. Every sample reimplements `string.Replace("{{var}}", value)` with no validation that all variables are substituted.

3. **Prompt versioning requires custom infrastructure.** S6.SnapVault built `AnalysisStyle` as an entity with `TemplateVersion`, `FullPromptOverride`, focus instructions, mandatory/optional field lists, and a factory to assemble them. This is a prompt entity in all but name. Every application needing prompt versioning will rebuild this pattern.

4. **Prompt iteration requires code deploys.** When Marta (domain expert persona) wants to refine an extraction prompt, she files a ticket. Priya changes a string constant, commits, deploys. The feedback cycle is days when it should be minutes. S6.SnapVault partially solved this with database-backed `AnalysisStyle`, but the solution is application-specific and not reusable.

The `System.Uri` type provides the design inspiration. `Uri.Parse("https://api.example.com:8080/v2/chat?model=llama3")` takes a string and produces an immutable object with `.Scheme`, `.Host`, `.Port`, `.Path`, `.Query` — structure extracted from a flat representation. The string is the source of truth, but the object makes it workable. `Uri` supports implicit conversion from string, round-trips losslessly, and is immutable.

`Prompt` applies the same philosophy: a string goes in; a rich, inspectable, immutable value comes out. Variables are extracted. The prompt round-trips losslessly. Backward compatibility is preserved through implicit conversion.

### Existing Patterns to Subsume

**S6.SnapVault's `AnalysisPromptFactory`** (`samples/S6.SnapVault/Services/AI/AnalysisPromptFactory.cs`) — A factory that assembles prompts from a base template constant, injects `{{FOCUS_INSTRUCTIONS}}`, `{{MANDATORY_FACTS}}`, and `{{OPTIONAL_FACTS}}` via string replacement, and supports `{{photoId}}`, `{{width}}`, `{{height}}`, `{{camera}}`, `{{orientation}}` variable substitution via `SubstituteVariables()`. The factory + `AnalysisStyle` entity + seeder is ~400 lines of application-specific prompt infrastructure.

**S6.SnapVault's `AnalysisStyle`** (`samples/S6.SnapVault/Models/AnalysisStyle.cs`) — An `Entity<AnalysisStyle>` with `Name`, `Description`, `Priority`, `FocusInstructions`, `MandatoryFields`, `EmphasisFields`, `DeemphasizedFields`, `FullPromptOverride`, `TemplateVersion`, `IsActive`, `CreatedBy`. This is a `PromptEntry` prototype.

**`ChatOptions.SystemPrompt`** (`src/Koan.AI.Contracts/Options/ChatOptions.cs`) — The current string-based system prompt. `Prompt` subsumes this by carrying the system directive as a structured part.

**`ConnectionStringParser`** (`src/Koan.Core/Orchestration/ConnectionStringParser.cs`) — Parse/Build pattern inspiration. Connection strings are also "structured strings" that benefit from being parsed into typed parts while preserving the original format.

### Variable Syntax Decision: `{single}` over `{{double}}`

The existing SnapVault/Meridian convention uses `{{double braces}}`. This ADR standardizes on `{single braces}` for three reasons:

1. **C# alignment.** `{0}`, `{name:format}` in `string.Format` and composite formatting. Developers already read `{variable}` as "placeholder" in C#.
2. **Simplicity.** One character fewer. No ambiguity about whether `{x}` and `{{x}}` mean different things.
3. **Escaping is the rare case.** When literal braces around an identifier are needed, `{{escaped}}` produces `{escaped}` — the double-brace becomes the escape hatch, not the default.

Migration from `{{double}}` to `{single}` is addressed in Consequences.

## Decision

### Part 1: The `Prompt` Type — Immutable Value with Structure

`Prompt` is a sealed, immutable type that represents a prompt as a value. Like `Uri`, it extracts structure from a string while preserving the original text. Like `Uri`, it supports implicit conversion from string for zero-friction adoption.

```csharp
namespace Koan.AI.Prompt;

/// <summary>
/// An immutable, inspectable prompt value. Like System.Uri for AI prompts:
/// a string goes in, a structured object comes out. Round-trips losslessly.
/// </summary>
public sealed class Prompt
{
    // ── Core parts (analogous to Uri.Scheme, Uri.Host, etc.) ──────────

    /// <summary>Original string representation. Always non-null.</summary>
    public string Raw { get; }

    /// <summary>System directive (instruction to the model about its role/behavior).</summary>
    public string? System { get; }

    /// <summary>User message template (the primary instruction or question).</summary>
    public string? Template { get; }

    /// <summary>Extracted {variable} placeholder names, in order of appearance.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Constraints, guardrails, and rules applied to the output.</summary>
    public IReadOnlyList<string> Constraints { get; }

    /// <summary>Expected output structure (JSON schema derived from a type).</summary>
    public OutputSpec? OutputFormat { get; }

    /// <summary>Few-shot examples for in-context learning.</summary>
    public IReadOnlyList<Example> Examples { get; }

    /// <summary>Arbitrary metadata (author, version notes, tags).</summary>
    public IReadOnlyDictionary<string, string> Meta { get; }

    /// <summary>Default variable values, applied when Resolve() context omits them.</summary>
    public IReadOnlyDictionary<string, string> Defaults { get; }

    // ── Construction ──────────────────────────────────────────────────

    /// <summary>
    /// Implicit conversion from string. Zero-overhead for simple prompts.
    /// Extracts {variable} placeholders; stores raw text unchanged.
    /// </summary>
    public static implicit operator Prompt(string text);

    /// <summary>
    /// Parse a string into a Prompt. Shallow: extracts {variables}, stores raw.
    /// System/Template/Constraints come from the builder, not inferred from text.
    /// </summary>
    public static Prompt Parse(string text);

    /// <summary>
    /// Build a structured prompt with system directive, constraints, examples, etc.
    /// </summary>
    public static Prompt Create(Action<PromptBuilder> configure);

    /// <summary>
    /// Load a prompt from the PromptEntry entity catalog by name.
    /// Returns the latest active version by default.
    /// </summary>
    public static Task<Prompt> Load(string name);

    /// <summary>Load a specific version from the catalog.</summary>
    public static Task<Prompt> Load(string name, int version);

    /// <summary>Load using a deployment strategy (A/B test, canary, pinned).</summary>
    public static Task<Prompt> Load(string name, PromptStrategy strategy);

    // ── Resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Resolve all {variable} placeholders from an object's properties.
    /// Unresolved variables remain as {variable} in the output.
    /// </summary>
    public string Resolve(object? variables = null);

    /// <summary>
    /// Resolve from a typed entity. Property names map to variable names.
    /// Enables: prompt.Resolve(article) where article.Topic fills {topic}.
    /// </summary>
    public string Resolve<T>(T context);

    // ── Immutable modification ────────────────────────────────────────

    /// <summary>
    /// Returns a new Prompt with modifications applied. Original is unchanged.
    /// </summary>
    public Prompt With(Action<PromptBuilder> modify);

    // ── Analysis ──────────────────────────────────────────────────────

    /// <summary>
    /// Estimate token count for the resolved prompt.
    /// Uses model-specific tokenizer when available, cl100k_base as fallback.
    /// </summary>
    public int EstimateTokens(string? model = null);

    /// <summary>
    /// List variables that would remain unresolved given a context object.
    /// Useful for validation before calling an expensive API.
    /// </summary>
    public IReadOnlyList<string> UnresolvedVariables(object? context);

    // ── Conversion ────────────────────────────────────────────────────

    /// <summary>Produces the resolved or raw string representation.</summary>
    public override string ToString();

    /// <summary>
    /// Implicit conversion to string for backward compatibility.
    /// Existing code that accepts string continues to work.
    /// </summary>
    public static implicit operator string(Prompt p);
}
```

**Parsing strategy is SHALLOW by design:**

- `Prompt.Parse("You are a {role}. Answer about {product}.")` extracts `Variables = ["role", "product"]` via regex `\{([a-zA-Z_][a-zA-Z0-9_]*)\}` and stores the raw text. No NLP. No template engine. No AST.
- `System`, `Template`, `Constraints`, `Examples`, `OutputFormat` are populated only through the builder. A parsed-from-string prompt has `System = null`, `Template = Raw`, no constraints, no examples.
- Simple string prompts incur near-zero overhead: one regex scan, one allocation. The common case — `Prompt("just a string")` — must be free.

### Part 2: `PromptBuilder` — Structured Assembly

The builder provides the rich construction path. Parts are assembled into a coherent prompt with clear semantic boundaries.

```csharp
namespace Koan.AI.Prompt;

public sealed class PromptBuilder
{
    /// <summary>Set the system directive (model role/persona).</summary>
    public PromptBuilder System(string directive);

    /// <summary>Set the primary instruction/question template.</summary>
    public PromptBuilder Instruct(string template);

    /// <summary>Add one or more constraints/guardrails.</summary>
    public PromptBuilder Constrain(params string[] rules);

    /// <summary>Specify expected output format via JSON schema from type.</summary>
    public PromptBuilder OutputAs<T>();

    /// <summary>Specify expected output format with explicit schema.</summary>
    public PromptBuilder OutputAs(OutputSpec spec);

    /// <summary>Add a few-shot example (input/output pair).</summary>
    public PromptBuilder Example(string input, string output);

    /// <summary>Add a few-shot example with a typed output.</summary>
    public PromptBuilder Example<T>(string input, T output);

    /// <summary>Set a default value for a variable placeholder.</summary>
    public PromptBuilder Default(string variable, string value);

    /// <summary>Attach metadata (author, version notes, tags).</summary>
    public PromptBuilder Meta(string key, string value);
}
```

**Usage:**

```csharp
// Full structured prompt with all parts
var prompt = Prompt.Create(p => p
    .System("You are a {role} for {company}")
    .Instruct("Answer the customer's question about {product}")
    .Constrain("Be concise", "Max 3 sentences", "Cite sources when possible")
    .OutputAs<SupportResponse>()
    .Example(
        input: "How do I reset my password?",
        output: new SupportResponse { Answer = "Go to Settings > Security > Reset Password", Confidence = 0.95 })
    .Example(
        input: "What are your hours?",
        output: new SupportResponse { Answer = "We're open Monday-Friday, 9AM-5PM EST", Confidence = 0.99 })
    .Default("role", "support agent")
    .Default("company", "Acme Corp")
    .Meta("author", "marta@acme.com")
    .Meta("version", "3")
    .Meta("team", "customer-success"));

// Inspect before sending
prompt.Variables    // ["role", "company", "product"]
prompt.Constraints  // ["Be concise", "Max 3 sentences", "Cite sources when possible"]
prompt.Examples     // 2 examples
prompt.Defaults     // {"role": "support agent", "company": "Acme Corp"}
prompt.UnresolvedVariables(new { product = "Widget" })  // ["role", "company"] — but have defaults

// Resolve with overrides (explicit context wins over defaults)
var text = prompt.Resolve(new { product = "Widget", role = "senior analyst" });
// → role="senior analyst" (override), company="Acme Corp" (default), product="Widget" (context)
```

**Builder assembly produces a composed `Raw` string:**

```
[System]
You are a {role} for {company}

[Instructions]
Answer the customer's question about {product}

[Constraints]
- Be concise
- Max 3 sentences
- Cite sources when possible

[Output Format]
Respond as JSON matching this schema:
{"type":"object","properties":{"answer":{"type":"string"},"confidence":{"type":"number"}}}

[Examples]
Input: How do I reset my password?
Output: {"answer":"Go to Settings > Security > Reset Password","confidence":0.95}

Input: What are your hours?
Output: {"answer":"We're open Monday-Friday, 9AM-5PM EST","confidence":0.99}
```

The section markers (`[System]`, `[Instructions]`, etc.) are part of the `Raw` representation. When a builder-constructed prompt converts to string, the sections are rendered in a deterministic order. When a builder-constructed prompt is used with `Client.Chat()`, the framework separates `System` into `ChatOptions.SystemPrompt` and the remainder into the user message — the prompt carries the semantic intent, the framework maps to the wire format.

### Part 3: `OutputSpec` — Type-Driven Output Schema

```csharp
namespace Koan.AI.Prompt;

/// <summary>
/// Describes the expected output structure. Derived from a type via OutputAs<T>()
/// or specified explicitly.
/// </summary>
public sealed record OutputSpec
{
    /// <summary>JSON Schema representation of the expected output.</summary>
    public string JsonSchema { get; init; }

    /// <summary>The CLR type this spec was derived from (null if manually specified).</summary>
    public Type? SourceType { get; init; }

    /// <summary>Format instruction text appended to the prompt.</summary>
    public string FormatInstruction { get; init; }

    /// <summary>Create an OutputSpec from a type by reflecting its public properties.</summary>
    public static OutputSpec FromType<T>() => FromType(typeof(T));

    /// <summary>Create an OutputSpec from a type.</summary>
    public static OutputSpec FromType(Type type);
}
```

`OutputAs<T>()` reflects public properties of `T` to generate a JSON schema. This schema is included in the prompt text and, when the framework supports constrained decoding (AI-0022 Part 11, `Client.Chat<T>()`), is also passed as a response format constraint to the model API.

### Part 4: `Example` — Few-Shot Learning

```csharp
namespace Koan.AI.Prompt;

/// <summary>
/// A single few-shot example: an input/output pair that demonstrates expected behavior.
/// </summary>
public sealed record Example
{
    /// <summary>The example input (what the user would say).</summary>
    public string Input { get; init; }

    /// <summary>The example output (what the model should produce).</summary>
    public string Output { get; init; }
}
```

Examples are serialized into the prompt text during assembly. The framework does not manage example ordering or selection — the developer controls this through the builder. Future work (AI-0029 Eval) may support automatic example selection based on similarity to the current input.

### Part 5: `PromptEntry` — Entity-Backed Prompt Catalog

`PromptEntry` is a Koan entity, inheriting all entity capabilities: persistence across any data provider, querying, versioning, auditing.

```csharp
namespace Koan.AI.Prompt;

/// <summary>
/// A persisted, versionable prompt entity. Enables prompt editing without code deploys,
/// A/B testing, and audit trails. The framework counterpart to S6.SnapVault's AnalysisStyle.
/// </summary>
public class PromptEntry : Entity<PromptEntry>
{
    /// <summary>
    /// Logical name used for loading. Convention: kebab-case.
    /// Example: "support-response", "receipt-extractor", "photo-analysis"
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The prompt content. Either a raw prompt string or a serialized builder definition
    /// (detected by the presence of [System]/[Instructions] section markers).
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// Auto-incremented version within a name group.
    /// Prompt.Load("support-response") returns the latest active version.
    /// Prompt.Load("support-response", version: 3) returns a specific version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Lifecycle status. Only Active entries are returned by default Load().
    /// </summary>
    public PromptStatus Status { get; set; } = PromptStatus.Draft;

    /// <summary>
    /// Who authored or last edited this version.
    /// Maps to Marta persona (domain expert editing prompts without code).
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Free-form notes about this version (what changed, why, expected impact).
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Optional traffic weight for A/B testing (0.0-1.0).
    /// When PromptStrategy.ABTest is used, traffic is distributed proportionally.
    /// </summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>When this version was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When this version was last modified.</summary>
    public DateTime? UpdatedAt { get; set; }
}

public enum PromptStatus
{
    /// <summary>Being edited, not served to Load() by default.</summary>
    Draft,

    /// <summary>Active in production, returned by Load().</summary>
    Active,

    /// <summary>No longer served, preserved for audit/rollback.</summary>
    Retired
}
```

**Loading from the catalog:**

```csharp
// Latest active version (most common)
var prompt = await Prompt.Load("support-response");

// Specific version (for pinned deployments or debugging)
var prompt = await Prompt.Load("support-response", version: 3);

// With deployment strategy
var prompt = await Prompt.Load("support-response", PromptStrategy.ABTest);
var prompt = await Prompt.Load("support-response", PromptStrategy.Canary(0.1));
var prompt = await Prompt.Load("support-response", PromptStrategy.Latest);
var prompt = await Prompt.Load("support-response", PromptStrategy.Pinned(3));
```

**Creating/updating entries (Marta's workflow — no code deploy):**

```csharp
// Create a new prompt version via entity API or admin UI
var entry = new PromptEntry
{
    Name = "support-response",
    Content = """
        [System]
        You are a support agent for {company}

        [Instructions]
        Answer the customer's question about {product}

        [Constraints]
        - Be concise
        - Max 3 sentences
        - Answer from knowledge base only
        """,
    Version = 4,
    Status = PromptStatus.Draft,
    Author = "marta@acme.com",
    Notes = "Added knowledge-base-only constraint after hallucination incident"
};
await entry.Save();

// Promote to active (retires previous active version automatically)
entry.Status = PromptStatus.Active;
await entry.Save();
```

### Part 6: `PromptStrategy` — Deployment Strategies

```csharp
namespace Koan.AI.Prompt;

/// <summary>
/// Controls how Prompt.Load() selects among multiple active versions.
/// </summary>
public abstract record PromptStrategy
{
    /// <summary>
    /// Distribute traffic across active versions proportional to their Weight.
    /// Requires 2+ active versions; degrades to Latest if only one exists.
    /// </summary>
    public static PromptStrategy ABTest => new ABTestStrategy();

    /// <summary>
    /// Route a percentage of traffic to the latest version, remainder to previous.
    /// Enables gradual rollout of new prompt versions.
    /// </summary>
    public static PromptStrategy Canary(double percentage) => new CanaryStrategy(percentage);

    /// <summary>
    /// Always return the highest-versioned active entry. Default behavior.
    /// </summary>
    public static PromptStrategy Latest => new LatestStrategy();

    /// <summary>
    /// Always return a specific version, regardless of status.
    /// For debugging, reproduction, and pinned deployments.
    /// </summary>
    public static PromptStrategy Pinned(int version) => new PinnedStrategy(version);
}

// Internal implementations — sealed, no public constructors
internal sealed record ABTestStrategy : PromptStrategy;
internal sealed record CanaryStrategy(double Percentage) : PromptStrategy;
internal sealed record LatestStrategy : PromptStrategy;
internal sealed record PinnedStrategy(int Version) : PromptStrategy;
```

**A/B test resolution:**

```csharp
// Two active versions with weights
// v3: Weight = 0.7 (established prompt)
// v4: Weight = 0.3 (experimental prompt with new constraint)

var prompt = await Prompt.Load("support-response", PromptStrategy.ABTest);
// 70% of calls → v3, 30% → v4
// Selection is deterministic per-request (hashed from request correlation ID when available)
```

**Canary resolution:**

```csharp
var prompt = await Prompt.Load("support-response", PromptStrategy.Canary(0.1));
// 10% of calls → latest active version (v4)
// 90% of calls → previous active version (v3)
```

The selected version is recorded in `Prompt.Meta["_version"]` and `Prompt.Meta["_strategy"]` for lineage tracking.

### Part 7: Integration with `Client` (AI-0021)

`Prompt` integrates with the existing `Client` facade. All existing string-based signatures continue to work unchanged — `Prompt`'s implicit conversion from string ensures full backward compatibility.

```csharp
// ── Existing code: unchanged, still works ─────────────────────────
await Client.Chat("Summarize: " + text);
await Client.Chat("Explain monads", new ChatOptions { Temperature = 0.3 });

// ── Prompt from string (implicit conversion) ──────────────────────
Prompt p = "Summarize this {topic} article: {content}";
await Client.Chat(p, new { topic = "AI", content = article.Content });

// ── Prompt with variables (new overload) ──────────────────────────
await Client.Chat(
    Prompt.Parse("Summarize this {topic} article: {content}"),
    new { topic = "AI", content = article.Content });

// ── Loaded prompt with entity context ─────────────────────────────
var prompt = await Prompt.Load("summarizer");
await Client.Chat(prompt, article);  // article.Topic → {topic}, article.Content → {content}

// ── Typed response (AI-0022 Part 11) ──────────────────────────────
var summary = await Client.Chat<Summary>(
    await Prompt.Load("summarizer"),
    article);

// ── Builder prompt with system directive ──────────────────────────
var prompt = Prompt.Create(p => p
    .System("You are a concise technical writer")
    .Instruct("Summarize the following article about {topic}")
    .Constrain("Max 3 bullet points", "Use simple language")
    .OutputAs<Summary>());

await Client.Chat(prompt, new { topic = "distributed systems" });
// Framework separates: System → ChatOptions.SystemPrompt
//                      Remainder → user message
//                      OutputAs<Summary> → response format constraint
```

**New `Client` overloads:**

```csharp
public static class Client
{
    // Existing (unchanged)
    public static Task<string> Chat(string message, CancellationToken ct = default);
    public static Task<string> Chat(string message, ChatOptions options, CancellationToken ct = default);

    // New: Prompt with variable context
    public static Task<string> Chat(Prompt prompt, object? variables = null, CancellationToken ct = default);
    public static Task<string> Chat(Prompt prompt, ChatOptions options, object? variables = null, CancellationToken ct = default);

    // New: Typed response with Prompt
    public static Task<T> Chat<T>(Prompt prompt, object? variables = null, CancellationToken ct = default);
    public static Task<T> Chat<T>(Prompt prompt, ChatOptions options, object? variables = null, CancellationToken ct = default);

    // New: Streaming with Prompt
    public static IAsyncEnumerable<string> Stream(Prompt prompt, object? variables = null, CancellationToken ct = default);
}
```

When `Client.Chat()` receives a `Prompt` with a non-null `System`, it automatically populates `ChatOptions.SystemPrompt` (unless the caller explicitly set one in `ChatOptions`, in which case the explicit option wins). The `OutputFormat` similarly maps to the response format constraint if the adapter supports it.

### Part 8: Integration with `Chain` (AI-0026)

Chains can reference prompts by name (loaded from catalog) or inline.

```csharp
// ── Named prompt from catalog ─────────────────────────────────────
var chain = Chain.Create()
    .WithPrompt("support-response")          // Loaded from PromptEntry catalog
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5)
    .Chat("{question}");

// ── Inline builder prompt ─────────────────────────────────────────
var chain = Chain.Create()
    .WithPrompt(Prompt.Create(p => p
        .System("You are a support agent")
        .Constrain("Answer from context only", "Cite article IDs")))
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5)
    .Chat("{question}");

// ── Chain with prompt strategy ────────────────────────────────────
var chain = Chain.Create()
    .WithPrompt("support-response", PromptStrategy.ABTest)
    .Retrieve<KnowledgeArticle>(query: "{question}", topK: 5)
    .Chat("{question}");

var answer = await chain.Run(new { question = "What is the refund policy?" });
```

`Chain.WithPrompt()` sets the chain's prompt context. The prompt's system directive and constraints are applied to subsequent `.Chat()` steps. Variables in the prompt are resolved from the chain's input context.

### Part 9: Integration with `[MediaAnalysis]` (AI-0027)

`[MediaAnalysis]` gains a `Prompt` property that loads a `PromptEntry` for the extraction instruction. This is the framework replacement for S6.SnapVault's `AnalysisPromptFactory` + `AnalysisStyle` pattern.

```csharp
// ── Attribute-driven prompt loading ───────────────────────────────

[StorageBinding(Profile = "cold", Container = "photos")]
[MediaAnalysis(
    Analysis = MediaAnalysis.Extract,
    Prompt = "photo-analysis")]                    // ← Loads PromptEntry by name
[Embedding]
public class PhotoAsset : MediaEntity<PhotoAsset>
{
    public string? AiDescription { get; set; }
    public string? OcrText { get; set; }
    public float[]? Embedding { get; set; }
}

// ── What happens on Upload() ──────────────────────────────────────

// 1. Bytes stored via IStorageProvider
// 2. [MediaAnalysis] loads Prompt.Load("photo-analysis")
// 3. Prompt resolved with entity context (width, height, format metadata)
// 4. Vision model processes image + resolved prompt
// 5. Results written to entity properties
// 6. [Embedding] feeds on analysis text
// 7. Entity + vector saved atomically
```

**Domain expert workflow (Marta persona):**

```csharp
// Marta edits the "photo-analysis" prompt via admin UI or API
// No code deploy required — next upload uses the new prompt version

var entry = await PromptEntry.Get("photo-analysis-v5-id");
entry.Content = """
    Analyze the image and describe what you see.
    Focus on: composition, lighting, subject, mood.
    Output JSON with tags, summary, and facts.
    """;
entry.Notes = "Simplified prompt — removed over-specific field instructions";
await entry.Save();

// Optionally: re-analyze existing photos with updated prompt
// [MediaAnalysis(Version = 5)] triggers re-analysis on next access
```

### Part 10: Lineage Integration

When `Model.Audit()` (AI-0022) is called, the active `Prompt` version at inference time is included in the lineage record. This enables full reproducibility: given the same model, the same prompt version, and the same input data, the output is reproducible.

```csharp
// Lineage record includes prompt provenance
var audit = await Model.Audit(modelRef);
// audit.Lineage.PromptName = "support-response"
// audit.Lineage.PromptVersion = 4
// audit.Lineage.PromptStrategy = "ABTest"
// audit.Lineage.PromptHash = "sha256:a1b2c3..."  // Content hash for integrity

// Eval.Compare() uses prompt lineage to ensure fair comparison
var comparison = await Eval.Compare(
    models: ["acme-support:v3", "acme-support:v4"],
    prompt: await Prompt.Load("support-response", PromptStrategy.Pinned(4)),
    data: testSet);
// Both models evaluated with the exact same prompt version
```

The prompt hash is computed from `Raw` content and stored alongside model lineage. When combined with `DatasetRef` (AI-0022) and `ModelRef`, this provides a complete provenance chain: `Data + Prompt + Model → Output`.

### Part 11: Variable Resolution Mechanics

Variable resolution follows a clear precedence chain:

```
1. Explicit context (from Resolve() argument)      ← highest priority
2. Default values (from PromptBuilder.Default())
3. Unresolved (left as {variable} in output)        ← lowest priority
```

**Resolution from anonymous objects:**

```csharp
var prompt = Prompt.Parse("Hello {name}, welcome to {place}");
prompt.Resolve(new { name = "Alice", place = "Wonderland" });
// → "Hello Alice, welcome to Wonderland"
```

**Resolution from entities:**

```csharp
var prompt = Prompt.Parse("Summarize this article about {Topic}: {Content}");
prompt.Resolve(article);
// → Property names matched case-insensitively to variable names
// → article.Topic → {Topic}, article.Content → {Content}
```

**Resolution from dictionaries:**

```csharp
var prompt = Prompt.Parse("Translate {text} to {language}");
prompt.Resolve(new Dictionary<string, string>
{
    ["text"] = "Hello world",
    ["language"] = "Japanese"
});
```

**Partial resolution (for chained/deferred resolution):**

```csharp
var prompt = Prompt.Parse("You are a {role}. Answer about {product}. Output: {format}");
var partial = prompt.Resolve(new { role = "analyst" });
// → "You are a analyst. Answer about {product}. Output: {format}"
// Unresolved variables remain for downstream resolution (e.g., Chain steps)
```

Property-to-variable matching uses case-insensitive comparison. For entities, only public readable properties are considered. Navigation properties, `byte[]`, and collection properties are skipped (same convention as AI-0021 Part 3 Chat entity context).

### Part 12: Token Estimation

```csharp
var prompt = Prompt.Create(p => p
    .System("You are a support agent for Acme Corp")
    .Instruct("Answer the customer's question: {question}")
    .Constrain("Max 3 sentences"));

// Estimate before resolution (with variables as-is)
var rough = prompt.EstimateTokens();
// → ~35 tokens

// Estimate after resolution
var resolved = prompt.Resolve(new { question = longCustomerMessage });
var accurate = Prompt.Parse(resolved).EstimateTokens("llama3.1");
// → 847 tokens (model-specific tokenizer)

// Use for budget checks before expensive API calls
if (prompt.EstimateTokens("gpt-4") > 4000)
{
    // Switch to a summarization step first
}
```

Token estimation is best-effort. The framework ships with cl100k_base (GPT-4/GPT-3.5 tokenizer) as the default. Model-specific tokenizers are loaded when available via the adapter — `IChatAdapter` gains an optional `EstimateTokens(string text)` method that adapters may implement.

### Part 13: Migration from `{{double}}` to `{single}` Braces

Existing applications (S6.SnapVault, S7.Meridian) use `{{variable}}` syntax. The migration path:

**Phase 1: Dual support (non-breaking)**

`Prompt.Parse()` recognizes both `{variable}` and `{{variable}}` patterns. When `{{variable}}` is detected, the framework:
1. Extracts the variable name
2. Logs a one-time deprecation notice per prompt: `"Prompt uses {{double brace}} syntax which is deprecated. Migrate to {single brace}. See AI-0025."`
3. Normalizes internally to `{variable}` for resolution

**Phase 2: Codemod helper**

```csharp
// One-time migration utility
PromptMigration.RewriteDoubleBraces("samples/S6.SnapVault/");
// Scans .cs files for {{identifier}} patterns in string literals
// Rewrites to {identifier}
// Reports changes to console
```

**Phase 3: Deprecation removal (future major version)**

`{{variable}}` support removed. `{{x}}` becomes the escape hatch (produces literal `{x}` in output).

**SnapVault-specific migration path:**

```csharp
// Before (AnalysisPromptFactory.cs)
prompt = prompt.Replace("{{FOCUS_INSTRUCTIONS}}", focusInstructions);
prompt = prompt.Replace("{{MANDATORY_FACTS}}", mandatoryFacts);
prompt = prompt.Replace("{{photoId}}", context.PhotoId);

// After (using Prompt)
var prompt = await Prompt.Load("photo-analysis");
var resolved = prompt.Resolve(new
{
    FOCUS_INSTRUCTIONS = focusInstructions,
    MANDATORY_FACTS = mandatoryFacts,
    photoId = context.PhotoId,
    width = context.Width,
    height = context.Height,
    camera = context.CameraModel ?? "Unknown",
    orientation = GetOrientation(context.AspectRatio)
});
```

The `AnalysisStyle` entity migrates to `PromptEntry`:

| `AnalysisStyle` field | `PromptEntry` equivalent |
|---|---|
| `Name` | `Name` |
| `Description` | `Notes` |
| `FocusInstructions` | Part of `Content` (system/constraint sections) |
| `MandatoryFields` / `EmphasisFields` / `DeemphasizedFields` | Encoded in `Content` template structure |
| `FullPromptOverride` | A `PromptEntry` version with different `Content` |
| `TemplateVersion` | `Version` (auto-incremented) |
| `IsActive` | `Status` (Active / Retired) |
| `IsSystemStyle` / `IsUserCreated` | `Author` + `Meta` convention |
| `CreatedAt` / `UpdatedAt` / `CreatedBy` | `CreatedAt` / `UpdatedAt` / `Author` |

`AnalysisPromptFactory` is replaced entirely by `Prompt.Load()` + `Prompt.Resolve()`. The factory pattern (base template + style customization + variable substitution) maps to `PromptEntry` (base versions) + `Prompt.With()` (customization) + `Prompt.Resolve()` (substitution).

### Part 14: Package Structure

```
Koan.AI.Prompt
├── Prompt.cs                  // The core type
├── PromptBuilder.cs           // Builder for structured assembly
├── PromptParser.cs            // Regex-based shallow parser
├── PromptResolver.cs          // Variable resolution engine
├── PromptSerializer.cs        // Builder ↔ section-marker format
├── OutputSpec.cs              // JSON schema from type
├── Example.cs                 // Few-shot example record
├── PromptEntry.cs             // Entity for catalog persistence
├── PromptStatus.cs            // Draft/Active/Retired enum
├── PromptStrategy.cs          // A/B test, Canary, Latest, Pinned
├── PromptLoader.cs            // Load() implementation (entity query + strategy)
├── PromptMigration.cs         // {{double}} → {single} codemod utility
├── TokenEstimator.cs          // Token counting with model-specific fallback
└── Extensions/
    └── ServiceCollectionExtensions.cs  // DI registration
```

`Koan.AI.Prompt` has no dependency on `Koan.AI` — it is a standalone package. `Koan.AI` depends on `Koan.AI.Prompt` to provide the `Client` integration overloads. This enables usage of `Prompt` as a value type in contexts that don't need the full AI inference surface (e.g., prompt management tools, admin UIs, testing utilities).

The `PromptEntry` entity depends on `Koan.Data.Core` (for `Entity<T>`). When no data provider is configured, `Prompt.Load()` throws with guidance to configure a provider or use inline prompts.

### Part 15: Persona Alignment

| Persona | Primary Interaction | Value Delivered |
|---------|-------------------|----------------|
| **Priya** (Software Engineer) | `Prompt("inline {text}")`, `Prompt.Create()`, `Client.Chat(prompt, vars)` | Type-safe variables, inspectable structure, IDE completion |
| **Marta** (Domain Expert) | `PromptEntry` via admin UI/API, edit `Content`, set `Status` | Edit prompts without code deploys, version history, rollback |
| **Riku** (AI Scientist) | `PromptStrategy.ABTest`, `Eval.Compare()` with pinned prompts | Measure prompt variants, fair A/B comparison, reproducibility |
| **Dana** (Integration) | `Model.Audit()` lineage includes prompt version + hash | Full provenance chain: data + prompt + model = output |

## Consequences

### Positive

- **String backward compatibility preserved.** Every existing `Client.Chat("text")` call site works without modification. Implicit conversion means zero migration cost for teams that don't need prompt structure.
- **Eliminates per-application prompt infrastructure.** S6.SnapVault's `AnalysisPromptFactory` + `AnalysisStyle` + `AnalysisStyleSeeder` + `IAnalysisPromptFactory` (~400 lines across 4 files) is replaced by `Prompt.Load()` + `Prompt.Resolve()` — framework primitives that every application gets for free.
- **Domain experts gain independence.** Marta edits `PromptEntry` content via API or admin UI. No code change, no PR, no deploy. The feedback cycle drops from days to minutes.
- **Prompt experimentation becomes measurable.** `PromptStrategy.ABTest` + `Eval.Compare()` (AI-0029) enables data-driven prompt improvement. Riku pins prompt versions for fair comparison.
- **Full lineage for reproducibility.** Prompt version + hash in the audit trail means any AI output can be reproduced by replaying the same model, prompt version, and input data.
- **Variable convention standardized.** `{single brace}` aligns with C# conventions. One pattern across all applications replaces per-project string replacement.
- **Immutable by design.** `Prompt` is sealed and all properties are read-only. `With()` returns a new instance. No mutation bugs, safe for concurrent use, safe to cache.
- **Progressive disclosure.** Simple use case (`Prompt("text")`) has near-zero overhead. Builder adds structure when needed. Catalog adds persistence when needed. Strategy adds experimentation when needed. Each tier is independently valuable.
- **Package independence.** `Koan.AI.Prompt` has no dependency on the inference surface. Prompt management tools, testing utilities, and admin UIs can use it without pulling in `Koan.AI`.

### Negative / Trade-offs

- **Variable syntax migration.** Existing `{{double brace}}` usage in S6.SnapVault and S7.Meridian must migrate. Mitigated by dual-support phase and codemod utility, but migration is still work.
- **`Prompt.Load()` requires a data provider.** Applications using inline prompts only don't need this, but the dependency on `Koan.Data.Core` exists in the package. Mitigated by clear error message when `Load()` is called without a configured provider.
- **Token estimation is approximate.** Different models use different tokenizers. The framework ships cl100k_base as a reasonable default but cannot guarantee accuracy for all models. Mitigated by adapter-level `EstimateTokens()` for supported providers.
- **Builder-assembled prompts have a fixed section format.** The `[System]`, `[Instructions]`, `[Constraints]` markers are a framework convention. Models don't have a universal standard for prompt structure. Mitigated by the framework decomposing the prompt into model-appropriate wire format (system message vs user message) during `Client.Chat()`.
- **A/B test traffic distribution relies on request correlation.** Without a correlation ID, distribution is random per-call, which may produce slightly uneven splits for low-traffic applications. Mitigated by falling back to `Random` when no correlation ID is available.
- **`PromptEntry` adds a data dependency for prompt versioning.** Teams not using entity persistence must use inline prompts or provide their own `PromptEntry` store. This is consistent with Koan's entity-first philosophy but may be unexpected for teams using Koan.AI without Koan.Data.

## References

- AI-0021: Category-Driven AI with Convention Defaults (`Client` facade, `ChatOptions.SystemPrompt`)
- AI-0022: Unified AI Lifecycle Vision (Part 4 sketches `Prompt()`, Part 11 mentions `Client.Chat<T>()`)
- AI-0026: Chain Composition Primitives (`Chain.WithPrompt()` integration)
- AI-0027: MediaAnalysis Attribute (`[MediaAnalysis(Prompt = "name")]` integration)
- AI-0029: Eval Quality Enforcement (`Eval.Compare()` with pinned prompts)
- DATA-0057: Core SingleFlight Infrastructure (deduplication for concurrent `Prompt.Load()`)
- `samples/S6.SnapVault/Services/AI/AnalysisPromptFactory.cs` — Factory pattern subsumed by `Prompt`
- `samples/S6.SnapVault/Services/AI/IAnalysisPromptFactory.cs` — Interface subsumed by `Prompt`
- `samples/S6.SnapVault/Models/AnalysisStyle.cs` — Entity prototype for `PromptEntry`
- `samples/S6.SnapVault/Initialization/AnalysisStyleSeeder.cs` — Seeder pattern replaced by `PromptEntry` data seeding
- `src/Koan.AI/Client.cs` — Extended with `Prompt`-accepting overloads
- `src/Koan.AI.Contracts/Options/ChatOptions.cs` — `SystemPrompt` property bridged from `Prompt.System`
- `src/Koan.Core/Orchestration/ConnectionStringParser.cs` — Parse/Build pattern inspiration
