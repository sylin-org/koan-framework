# Defensive Publication: Category-Scoped AI Provider Routing via AsyncLocal Immutable Stack

**Publication Type:** Defensive Publication (Prior Art Disclosure)
**Title:** Category-Scoped AI Provider Routing via AsyncLocal Immutable Stack with Multi-Tier Resolution Chain
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Date of Disclosure:** 2026-03-24
**Framework:** Koan Framework v0.6.3 (.NET, target net10.0)
**Repository:** github.com/koan-framework (private)
**Governing ADRs:** AI-0021 (Category-Driven AI with Convention Defaults), AI-0032 (Intent-Capability Resolution with Recipes)

---

## 1. Technical Problem Addressed

Modern applications increasingly consume multiple AI providers simultaneously for different operation types within the same logical code block. A single HTTP request handler might generate a chat completion from a local Ollama GPU instance, produce a vector embedding via an OpenAI cloud API, and run OCR through an Azure Document Intelligence endpoint. Each operation category has distinct cost profiles, latency characteristics, quality-of-service requirements, and provider ecosystems.

Existing AI frameworks force developers to solve this routing problem through one of three inadequate mechanisms:

**A. Per-call explicit provider selection.** Every invocation carries an explicit provider identifier or client reference. This couples business logic to infrastructure concerns, defeats encapsulation, and makes provider migration a global search-and-replace exercise. When the same operation category (e.g., "Embed") should route to different providers based on deployment environment, the per-call approach demands conditional branching or factory indirection at every call site.

**B. Singleton provider binding.** A single AI client is configured for the entire application (or per dependency-injection scope). Frameworks such as Spring AI bind one `ChatClient` per bean; Semantic Kernel registers a single `IChatCompletionService` in the DI container. Routing different categories to different providers requires manual orchestration outside the framework's model.

**C. Chain-level provider assignment.** Orchestration frameworks like LangChain assign a model to an entire chain or pipeline. There is no mechanism to route one operation within the chain to provider A and another operation within the same chain to provider B based on the operation's semantic category.

None of these approaches provide **ambient, per-category, nestable provider routing** that flows transparently across asynchronous execution boundaries. The developer is left to build ad-hoc routing infrastructure for each project.

A secondary problem compounds the first: the resolution of which provider serves a given category involves multiple stakeholders (developer, ML engineer, DevOps operator, automated orchestrator, framework defaults) whose preferences must compose in a deterministic priority order. No existing framework defines such a resolution chain, let alone provides an extensible mechanism for adding new resolution layers.

---

## 2. Novel Solution Description

The invention introduces a **category-scoped ambient routing system** for AI operations that combines three mechanisms into a unified resolution architecture:

### 2.1. AsyncLocal Immutable Stack for Ambient Category Routing

An `AsyncLocal<ImmutableStack<AiCategoryScope>>` serves as the ambient routing context. Each `AiCategoryScope` entry on the stack contains per-category routing overrides (source and model) for a fixed set of AI categories (Chat, Embed, Ocr, Vision, etc.), plus an optional "all" override that applies to every category.

The critical design properties are:

**Immutability.** The stack uses `System.Collections.Immutable.ImmutableStack<T>`. Push and pop operations produce new stack instances rather than mutating the existing one. This prevents race conditions when async continuations resume on different threads, because `AsyncLocal<T>` captures the value per logical call context and immutable values cannot be corrupted by concurrent access.

**Category-granular scoping.** Each stack entry specifies overrides for individual categories independently. A scope can override `Embed` without affecting `Chat`. When resolving, the stack is walked top-to-bottom (innermost scope first) per category independently. The first non-null value for a given category wins. This means nested scopes compose naturally: an outer scope setting `Chat = "gpt-4"` and an inner scope setting `Embed = "local-model"` result in chat routing to gpt-4 and embed routing to local-model, with neither scope needing to know about the other's overrides.

**Disposable lifecycle.** `Client.Scope()` returns an `IDisposable` that pops the scope from the stack on disposal. The `using` block idiom guarantees stack cleanup even when exceptions occur. The implementation validates that the scope being disposed is still the top of the stack (reference equality check) to detect misuse patterns such as disposing scopes out of order.

**Async flow transparency.** Because `AsyncLocal<T>` flows across `await` boundaries, scopes established in a parent method are visible in all called methods, including across task parallelism boundaries within the same logical call chain. This eliminates the need to pass routing context as a parameter through call hierarchies.

The actual implementation in `AiCategoryScope.cs`:

```csharp
public sealed class AiCategoryScope : IDisposable
{
    private static readonly AsyncLocal<ImmutableStack<AiCategoryScope>> _contextStack = new();

    private readonly string? _all;
    private readonly CategoryOverride? _chat;
    private readonly CategoryOverride? _embed;
    private readonly CategoryOverride? _ocr;
    private readonly ImmutableStack<AiCategoryScope> _previousStack;
    private bool _disposed;

    internal AiCategoryScope(
        string? all = null,
        string? chatSource = null, string? chatModel = null,
        string? embedSource = null, string? embedModel = null,
        string? ocrSource = null, string? ocrModel = null)
    {
        _all = all;
        _chat = (chatSource ?? chatModel) is not null
            ? new CategoryOverride(chatSource, chatModel) : null;
        _embed = (embedSource ?? embedModel) is not null
            ? new CategoryOverride(embedSource, embedModel) : null;
        _ocr = (ocrSource ?? ocrModel) is not null
            ? new CategoryOverride(ocrSource, ocrModel) : null;

        _previousStack = _contextStack.Value ?? ImmutableStack<AiCategoryScope>.Empty;
        _contextStack.Value = _previousStack.Push(this);
    }

    public static string? ResolveSource(string category)
    {
        var stack = _contextStack.Value;
        if (stack is null || stack.IsEmpty) return null;

        foreach (var scope in stack)
        {
            var categoryOverride = scope.GetCategoryOverride(category);
            if (categoryOverride?.Source is not null)
                return categoryOverride.Source;
            if (scope._all is not null)
                return scope._all;
        }
        return null;
    }

    public static string? ResolveModel(string category)
    {
        var stack = _contextStack.Value;
        if (stack is null || stack.IsEmpty) return null;

        foreach (var scope in stack)
        {
            var categoryOverride = scope.GetCategoryOverride(category);
            if (categoryOverride?.Model is not null)
                return categoryOverride.Model;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        var current = _contextStack.Value;
        if (current is not null && !current.IsEmpty
            && ReferenceEquals(current.Peek(), this))
        {
            _contextStack.Value = _previousStack;
        }
        _disposed = true;
    }

    private sealed record CategoryOverride(string? Source, string? Model);
}
```

### 2.2. Seven-Layer Resolution Chain

The `AiCategoryRouter` resolves the concrete provider and model for each AI operation through a deterministic priority chain. Each layer either returns a binding or yields to the next layer. The chain is:

| Priority | Layer | Stakeholder | Mechanism |
|----------|-------|-------------|-----------|
| 1 | Explicit call-site option (`ChatOptions.Model`) | Developer | Per-request parameter |
| 2 | Ambient `AiCategoryScope` stack (innermost wins) | Developer | `AsyncLocal<ImmutableStack>` walk |
| 3 | Active recipe binding (`IAiRecipeProvider`) | ML Engineer / DevOps | Configuration section `Koan:Ai:Recipes:{name}` |
| 4 | Orchestrator advisor (`IAiModelAdvisor`) | Automated system | Runtime recommendation from infrastructure |
| 5 | Per-category configuration (`Koan:Ai:{Category}:Model`) | Operator | `appsettings.json` |
| 6 | Source/member default model | Framework | Adapter-reported defaults |
| 7 | Hardcoded category fallback | Framework | Compile-time constant |

The router implementation in `AiCategoryRouter.Resolve()`:

```csharp
public AiRouteResolution Resolve(string category,
    string? sourceHint = null, string? modelHint = null)
{
    // Layer 2: AsyncLocal scope stack
    var (scopeSource, scopeModel) = AiCategoryScope.ResolveMerged(
        category, sourceHint, modelHint);

    // Category config defaults (Layer 5)
    var categoryOptions = GetCategoryOptions(category);
    var effectiveSource = scopeSource ?? categoryOptions?.Source;

    // Layer 3: Recipe (human-curated, sits between scope and advisor)
    var recipeModel = scopeModel is null
        ? _recipe?.GetModel(category) : null;

    // Layer 4: Orchestrator advisor (automated recommendation)
    var advisorModel = scopeModel is null && recipeModel is null
        ? _advisor?.GetRecommendedModel(category) : null;

    // Merge: first non-null wins in priority order
    var effectiveModel = scopeModel
        ?? recipeModel
        ?? advisorModel
        ?? categoryOptions?.Model
        ?? definition.DefaultModel;

    // Via delegation for task categories...
    // Source resolution, member selection, adapter lookup...

    return new AiRouteResolution(source, member, adapter,
        category, resolvedModel);
}
```

The novel contribution is that each layer in the chain serves a distinct persona (developer, ML engineer, automated system, operator) with a clean separation of concerns. Layers are pluggable: `IAiRecipeProvider` and `IAiModelAdvisor` are optional DI registrations. When absent, their layers are skipped with zero overhead.

### 2.3. Two-Tier Category Architecture with Via Delegation

AI categories are organized into two tiers:

**Protocol categories** (Chat, Embed) represent fundamentally different wire protocols with distinct input/output types and dedicated adapter interfaces (`IChatAdapter`, `IEmbedAdapter`).

**Task categories** (Ocr, Vision) represent specialized operations that may delegate to a protocol category when no dedicated adapter is registered. The `Via` property on a category definition specifies the delegation target.

```csharp
internal static readonly IReadOnlyDictionary<string, AiCategoryDefinition> Categories =
    new Dictionary<string, AiCategoryDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        [AiCapability.Chat] = new()
        {
            Name = AiCapability.Chat,
            AdapterInterface = typeof(IChatAdapter),
        },
        [AiCapability.Embed] = new()
        {
            Name = AiCapability.Embed,
            AdapterInterface = typeof(IEmbedAdapter),
        },
        [AiCapability.Ocr] = new()
        {
            Name = AiCapability.Ocr,
            AdapterInterface = typeof(IOcrAdapter),
            Via = AiCapability.Chat,
            DefaultModel = "glm-ocr",
        },
    };
```

When resolving an OCR operation, the router first checks whether an `IOcrAdapter` is registered. If one exists (e.g., Azure Document Intelligence), it routes directly. If none exists, it recursively resolves via the Chat category, passing the OCR model and image payload through the chat adapter's multimodal message format. This delegation is invisible to the caller -- `Client.Ocr(imageBytes)` produces the same result regardless of whether a dedicated OCR adapter is present.

### 2.4. Recipe Layer for Human-Curated Model Selection

The `IAiRecipeProvider` mechanism introduces a declarative, versionable, environment-scoped artifact for model selection. Recipes are sparse capability-to-model maps stored in `appsettings.json`:

```json
{
  "Koan": {
    "Ai": {
      "ActiveRecipe": "production-balanced",
      "Recipes": {
        "production-balanced": {
          "Chat": "qwen3.5:9b",
          "Embed": "nomic-embed-text",
          "Vision": "llava:13b"
        },
        "cost-optimized": {
          "Chat": "phi4:3.8b",
          "Embed": "nomic-embed-text"
        }
      }
    }
  }
}
```

A recipe is "sparse" -- omitting a capability means "no opinion," allowing the next resolution layer (advisor, config, default) to decide. Recipes are "named" -- enabling git diff, A/B testing, and environment-scoped selection via standard .NET configuration layering (`appsettings.Production.json`). Recipes sit between developer-controlled scopes (layers 1-2) and automated orchestrator recommendations (layer 4), giving ML engineers and DevOps a priority level that overrides automation but respects explicit developer intent.

### 2.5. Convention-Inferred Operation Defaults

Every category operation succeeds on any entity without decoration. The `EmbeddingMetadata.Resolve<T>()` method never returns null -- when no `[Embedding]` attribute is present, it infers metadata through a convention chain:

1. Public string properties concatenated with newline
2. Fallback to JSON serialization of all public readable properties
3. Fail with clear message only when no embeddable content exists

Attributes (`[Embedding]`, `[AiContext]`, `[Ocr]`) serve exclusively as lifecycle opt-ins (e.g., auto-embed on entity save), never as gates on on-demand operations. This eliminates the common framework anti-pattern where exploration requires upfront configuration.

---

## 3. Implementation Architecture

### 3.1. Component Diagram

```
                     Developer Code
                          |
                    Client.Scope()
                          |
                 AiCategoryScope
              (AsyncLocal ImmutableStack)
                          |
                    Client.Chat() / Client.Embed() / Client.Ocr()
                          |
                   AiCategoryRouter.Resolve(category)
                          |
        +--------+--------+--------+--------+--------+--------+
        |        |        |        |        |        |        |
     Layer 1  Layer 2  Layer 3  Layer 4  Layer 5  Layer 6  Layer 7
     (call    (scope   (recipe) (advisor)(config) (source) (fallback)
      site)    stack)                               default)
        |        |        |        |        |        |        |
        +--------+--------+--------+--------+--------+--------+
                          |
                    AiRouteResolution
                  (Source, Member, Adapter, Model)
                          |
              +-----+-----+-----+
              |           |           |
         IChatAdapter  IEmbedAdapter  IOcrAdapter
              |           |           |
         (provider-specific wire protocol)
```

### 3.2. DI Registration

All components are registered as singletons through `ServiceCollectionExtensions.AddAi()`:

```csharp
services.TryAddSingleton<IAiRecipeProvider>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetService<ILogger<AiRecipeProvider>>();
    return new AiRecipeProvider(configuration, logger);
});

services.TryAddSingleton(sp => new AiCategoryRouter(
    sp.GetRequiredService<IAiAdapterRegistry>(),
    sp.GetRequiredService<IAiSourceRegistry>(),
    sp.GetRequiredService<IOptions<AiOptions>>(),
    sp.GetService<IAiRecipeProvider>(),
    sp.GetService<IAiModelAdvisor>(),
    sp.GetService<ILogger<AiCategoryRouter>>()));
```

`IAiRecipeProvider` and `IAiModelAdvisor` are injected as optional dependencies (nullable). When not registered, their respective resolution layers are skipped. The `ZenGardenModelAdvisor` provides the `IAiModelAdvisor` implementation when the Zen Garden orchestrator is available, fetching recommendations from `/v1/recommendations` and caching them with configurable TTL.

### 3.3. Scope Stack Behavior Under Nesting

```csharp
// Stack: [empty]

using (Client.Scope(chat: "gpt-4"))
{
    // Stack: [{Chat=gpt-4}]
    // Resolve(Chat) → gpt-4
    // Resolve(Embed) → null (falls through to recipe/advisor/config)

    using (Client.Scope(embed: "local-model"))
    {
        // Stack: [{Embed=local-model}, {Chat=gpt-4}]
        // Resolve(Chat) → inner has no Chat override → walk to outer → gpt-4
        // Resolve(Embed) → inner has Embed override → local-model
    }
    // Stack: [{Chat=gpt-4}]
}
// Stack: [empty]
```

Each category resolves independently through the stack. The stack walk short-circuits at the first non-null value for the requested category. The "all" override acts as a catch-all at the scope level, yielding to any explicit category override in the same or a more-inner scope.

### 3.4. Vision-Aware Routing

The `ResolveChat` convenience method on `AiCategoryRouter` performs content-aware routing for multimodal requests. When a chat request contains image message parts and no explicit model is specified, the router consults the advisor for a vision-capable model:

```csharp
public AiRouteResolution ResolveChat(AiChatRequest request)
{
    var modelHint = request.Model;

    if (string.IsNullOrEmpty(modelHint) && _advisor is not null)
    {
        var hasImage = request.Messages?.Any(m =>
            m.Parts?.Any(p => string.Equals(p.Type, "image",
                StringComparison.OrdinalIgnoreCase)) == true) == true;

        if (hasImage)
        {
            var visionModel = _advisor.GetRecommendedModel(AiCapability.Vision);
            if (visionModel is not null)
                modelHint = visionModel;
        }
    }

    return Resolve(AiCapability.Chat, request.Route?.AdapterId, modelHint);
}
```

This cross-category awareness -- a Chat request inspecting its content to borrow the Vision category's model recommendation -- demonstrates how the category architecture enables intelligent routing decisions without exposing complexity to the caller.

### 3.5. Configuration Structure

```json
{
  "Koan": {
    "Ai": {
      "ActiveRecipe": "production-balanced",
      "Chat": {
        "Source": "ollama-local",
        "Model": "llama3.2"
      },
      "Embed": {
        "Source": "openai-embeddings",
        "Model": "text-embedding-3-large"
      },
      "Ocr": {
        "Source": "ollama-local",
        "Model": "glm-ocr",
        "Via": "Chat"
      },
      "Recipes": {
        "production-balanced": {
          "Chat": "qwen3.5:9b",
          "Embed": "nomic-embed-text",
          "Vision": "llava:13b",
          "Thinking": "qwq:32b",
          "Quick": "qwen3.5:1.7b"
        }
      },
      "Sources": {
        "ollama-local": {
          "Provider": "ollama",
          "Members": [
            { "Name": "ollama-local::host", "ConnectionString": "http://localhost:11434" }
          ]
        }
      }
    }
  }
}
```

---

## 4. Specific Claims of Novelty

The following elements, individually and in combination, constitute the novel contributions of this invention:

**Claim 1: Per-category ambient AI provider routing via AsyncLocal immutable stack.**
No prior system uses `AsyncLocal<ImmutableStack<T>>` where each stack entry contains independent per-category routing slots (Chat, Embed, Ocr, Vision, etc.) that resolve independently through a top-to-bottom stack walk. The combination of (a) per-category granularity within each scope, (b) immutable stack for thread-safe async flow, and (c) independent per-category resolution across nested scopes is novel.

**Claim 2: Seven-layer resolution chain with pluggable stakeholder-aligned layers.**
No prior AI framework defines a deterministic priority chain where each layer maps to a distinct stakeholder persona (developer > ML engineer > automated system > operator > framework defaults), with layers being independently pluggable DI registrations that skip cleanly when absent. The specific ordering -- call-site, ambient scope, recipe, advisor, config, source default, hardcoded fallback -- and the mechanism by which each layer returns null to yield to the next is novel in the AI framework domain.

**Claim 3: Recipe-based declarative model selection as a resolution chain layer.**
No prior framework provides named, sparse, versionable, environment-scoped model-to-capability mappings ("recipes") that sit at a defined priority level between developer scopes and automated infrastructure recommendations. The recipe mechanism -- where omitting a key means "no opinion" and the next layer decides -- combined with standard .NET configuration layering for environment scoping, is novel.

**Claim 4: Two-tier category architecture with transparent Via delegation.**
No prior framework distinguishes protocol categories (with distinct wire protocols and adapter interfaces) from task categories (that delegate to protocol categories when no dedicated adapter exists), where the delegation is transparent to the caller and determined at resolution time by checking the DI container for a dedicated adapter registration.

**Claim 5: Convention-inferred operation defaults where attributes gate lifecycle only.**
No prior framework uses entity attributes exclusively as lifecycle opt-ins (auto-embed on save, change detection, background processing) while allowing all on-demand operations (embedding, semantic search, OCR) to succeed on undecorated entities through convention inference chains. The specific inversion -- attributes restrict automation scope rather than enable operations -- is novel.

**Claim 6: Cross-category content-aware routing.**
The mechanism where a Chat category resolution inspects the request content (detecting image parts) to borrow the Vision category's recommended model from the advisor, without the caller specifying a vision model or vision category, is novel. The resolution stays within the Chat category but transparently selects a vision-capable model.

---

## 5. Comparison with Prior Art

### 5.1. LangChain (Python/JavaScript)

LangChain binds a single LLM to each chain or agent. The `ChatOpenAI` or `ChatOllama` class is instantiated with a specific model and passed to the chain constructor. There is no ambient routing context. To use different providers for different operation types within the same chain, developers must manually instantiate separate model objects and explicitly wire them to each chain step. LangChain has no concept of AI operation categories, no ambient scope mechanism, no multi-layer resolution chain, and no recipe system. Routing is fully explicit at construction time.

### 5.2. Semantic Kernel (Microsoft)

Semantic Kernel registers AI services (`IChatCompletionService`, `ITextEmbeddingGenerationService`) in its `Kernel` builder. Multiple services can be registered with service IDs, and a specific service can be selected per function invocation via `KernelArguments`. However: (a) there is no ambient scope -- selection must be explicit at each call site or function definition; (b) there is no immutable stack for nested overrides; (c) there is no multi-layer resolution chain with recipe/advisor/config layers; (d) there is no Via delegation for task categories. Semantic Kernel's approach is closest to this invention's Layer 1 (explicit call-site option) but lacks Layers 2-7.

### 5.3. Spring AI (Java)

Spring AI binds a single `ChatClient` per Spring bean. The `ChatClient.Builder` creates an immutable client tied to a specific model and provider. To use multiple providers, developers define multiple beans and inject the appropriate one. There is no ambient context, no per-category routing, no resolution chain beyond DI scope, and no recipe mechanism. The ThreadLocal pattern used in some Spring components is for request-scoped state, not for nested AI routing overrides.

### 5.4. Microsoft.Extensions.AI (ME.AI)

ME.AI defines `IChatClient` and `IEmbeddingGenerator<TInput, TEmbedding>` interfaces with a middleware pipeline pattern. Services are registered in DI and can be decorated with middleware (logging, caching, telemetry). However: (a) routing is determined at DI registration time, not at call time; (b) there is no ambient scope for per-call-block provider selection; (c) there is no multi-category awareness -- each interface is resolved independently through standard DI; (d) there is no resolution chain beyond what DI provides. The Koan framework actually implements ME.AI's interfaces (`AdapterBackedChatClient`, `AdapterBackedEmbeddingGenerator`) as the bottom layer, adding the category routing and resolution chain on top.

### 5.5. Haystack (Python)

Haystack uses a pipeline DAG where each component declares its inputs and outputs. Model selection is per-component at pipeline construction time. There is no ambient routing, no runtime scope overrides, and no multi-layer resolution chain. The pipeline topology is static once constructed.

### 5.6. AsyncLocal Usage in .NET Ecosystem

`AsyncLocal<T>` is used by `Activity` (OpenTelemetry/diagnostics), `HttpContext.Current` (legacy ASP.NET), and `LogContext` (Serilog) for ambient state. However, none of these use an immutable stack where each entry contains per-category routing slots that resolve independently. The combination of AsyncLocal + ImmutableStack + per-category independent resolution is not found in existing .NET frameworks for any domain, let alone AI provider routing.

---

## 6. Enabling Disclosure

### 6.1. Complete Source Files

The invention is fully implemented in the following source files within the Koan Framework v0.6.3 repository:

| File | Purpose |
|------|---------|
| `src/Koan.AI/Context/AiCategoryScope.cs` | AsyncLocal immutable stack with per-category scope resolution |
| `src/Koan.AI/Pipeline/AiCategoryRouter.cs` | Seven-layer resolution chain, Via delegation, vision-aware routing |
| `src/Koan.AI/Pipeline/AiRecipeProvider.cs` | Recipe configuration reader and model resolver |
| `src/Koan.Core/AI/IAiRecipeProvider.cs` | Recipe provider interface (resolution chain layer 3) |
| `src/Koan.Core/AI/IAiModelAdvisor.cs` | Advisor interface (resolution chain layer 4) |
| `src/Koan.ZenGarden/AI/ZenGardenModelAdvisor.cs` | Orchestrator-backed advisor implementation |
| `src/Koan.AI/ServiceCollectionExtensions.cs` | DI registration wiring all components |
| `docs/decisions/AI-0021-category-driven-ai-with-convention-defaults.md` | Full architectural decision record |
| `docs/decisions/AI-0032-intent-capability-resolution-with-recipes.md` | Recipe layer architectural decision record |

### 6.2. Reproduction Steps

To reproduce the invention:

1. **Define `AiCategoryScope`** as a sealed class with `AsyncLocal<ImmutableStack<AiCategoryScope>>` static field. Each instance holds per-category source/model overrides and an optional "all" override. The constructor pushes `this` onto the stack; `Dispose()` restores the previous stack.

2. **Define `AiCategoryRouter`** with a `Resolve(category, sourceHint, modelHint)` method that:
   - Calls `AiCategoryScope.ResolveMerged(category)` to get scope overrides (Layer 2)
   - Calls `IAiRecipeProvider.GetModel(category)` if scope returned no model (Layer 3)
   - Calls `IAiModelAdvisor.GetRecommendedModel(category)` if recipe returned null (Layer 4)
   - Falls through to category config, source default, and hardcoded fallback (Layers 5-7)
   - Checks for Via delegation on task categories and recurses if no dedicated adapter exists

3. **Define `AiRecipeProvider`** that reads `Koan:Ai:ActiveRecipe` and `Koan:Ai:Recipes:{name}` from `IConfiguration`. Stores bindings as `IReadOnlyDictionary<string, string>`. Returns null for unbound categories.

4. **Register components** as singletons with `IAiRecipeProvider` and `IAiModelAdvisor` as optional dependencies. When null, their resolution layers are skipped.

5. **Expose via static facade** (`Client.Scope()`, `Client.Chat()`, `Client.Embed()`, `Client.Ocr()`) that internally resolves through the router.

### 6.3. Key Design Constraints

- The immutable stack must be used (not a mutable stack or list) because `AsyncLocal<T>` semantics require that value changes in child contexts do not propagate to parent contexts. A mutable collection would be shared by reference and mutations would be visible across contexts.
- The "all" override in a scope has lower priority than explicit category overrides in the same scope but higher priority than any override in a parent scope. This ensures that `Scope(all: "X")` followed by `Scope(embed: "Y")` routes Embed to Y and everything else to X.
- Recipe model resolution is unconditional -- it returns the configured model string regardless of whether that model is actually available at runtime. Availability checking is the adapter's responsibility. This keeps the recipe layer simple and stateless.
- The `Via` delegation is recursive: a task category delegates to a protocol category, which goes through the full resolution chain for that protocol category. The effective model from the original task category resolution is passed as a hint to the protocol category resolution.

---

## 7. Antagonist Analysis

### 7.1. Challenge: "AsyncLocal for ambient state is well-known"

**Rebuttal:** The novelty is not in using `AsyncLocal<T>` for ambient state. The novelty is in the specific data structure (`ImmutableStack<AiCategoryScope>` where each entry contains independent per-category routing slots) and the resolution algorithm (per-category independent stack walk with "all" fallback). Existing uses of AsyncLocal in .NET (Activity, HttpContext, LogContext) carry a single value or a flat property bag, not a stack of multi-category routing overrides that resolve independently per category. The independent-per-category resolution across nested scopes is the distinguishing mechanism.

### 7.2. Challenge: "Priority chains for configuration resolution are common"

**Rebuttal:** Configuration priority chains (e.g., environment variables > appsettings > defaults) are indeed common. The novelty here is: (a) applying a priority chain specifically to AI model/provider resolution with layers that map to distinct stakeholder personas; (b) making layers pluggable via optional DI registrations that skip cleanly when absent; (c) including an ambient scope stack (Layer 2) and a recipe layer (Layer 3) as first-class resolution participants alongside configuration and automated recommendations. No existing AI framework defines this specific chain or provides this composition mechanism.

### 7.3. Challenge: "DI service selection achieves the same result"

**Rebuttal:** DI service selection (e.g., named services, keyed services in .NET 8+) determines routing at registration time or at explicit resolution time. It cannot provide: (a) ambient routing that changes mid-request based on code block scope; (b) nested overrides where inner scopes compose with outer scopes per-category; (c) a multi-layer fallback chain that includes runtime recommendations and declarative recipes. DI is used as the mechanism for wiring the components, but the routing decisions happen at a layer above DI, driven by ambient scope state that DI has no visibility into.

### 7.4. Challenge: "Via delegation is just a proxy pattern"

**Rebuttal:** Standard proxy patterns delegate unconditionally. The Via delegation here is conditional on runtime adapter availability (checked via `_adapterRegistry.All.Any(a => adapterInterface.IsInstanceOfType(a))`), recursive through the same resolution chain, and preserves the original task category's resolved model as a hint to the protocol category. The delegation decision is made at resolution time, not at registration time, allowing the same application to route OCR directly when an `IOcrAdapter` is registered or through Chat when one is not, without configuration changes.

### 7.5. Challenge: "This is obvious combination of known techniques"

**Rebuttal:** The individual components (AsyncLocal, immutable collections, configuration priority, DI, proxy delegation) are known. The non-obvious combination is: using an AsyncLocal immutable stack with per-category independent resolution as one layer in a seven-layer chain that includes a sparse recipe artifact and a runtime advisor, where task categories transparently delegate to protocol categories through the same chain, and entity operations succeed without decoration via convention inference. This specific architecture solves a problem (ambient per-category AI provider routing with multi-stakeholder resolution) that no existing framework addresses, and the solution's design choices (independent per-category stack walk, recipe sparsity semantics, conditional Via recursion, attribute-as-lifecycle-only-gate) are not obvious from the individual components.

### 7.6. Challenge: "Recipes are just another configuration section"

**Rebuttal:** Recipes are structurally a configuration section, but their semantic role in the resolution chain is novel. They sit between developer-controlled scopes (layers 1-2) and automated recommendations (layer 4) -- a priority level that does not exist in any prior framework. The sparsity semantics (missing key = "no opinion" rather than "use default") combined with named selection and environment-scoped activation via standard .NET configuration layering creates a declarative artifact type purpose-built for the ML engineer persona. This is not how configuration sections are typically used in AI frameworks.

---

**Statement of Defensive Publication Intent:**
This document is published as a defensive disclosure to establish prior art and prevent any party from obtaining patent protection on the techniques described herein. The inventor makes this disclosure to ensure that the described technology remains available for unrestricted use by the public. This publication is intended to serve as prior art effective as of the date of disclosure.

**Inventor Attestation:**
I, Leo Botinelly (Leonardo Milson Botinelly Soares), attest that the techniques described in this document are my original work, implemented in the Koan Framework, and are hereby disclosed to establish prior art as of 2026-03-24.
