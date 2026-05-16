---
id: AI-0032
slug: AI-0032-intent-capability-resolution-with-recipes
domain: AI
status: Proposed
date: 2026-03-24
---

# ADR: Intent-Capability Resolution with Pluggable Provider Layers and Recipes

**Contract**

- **Inputs:** Consumer intent (capability name: Chat, Embed, Vision, etc.); provider chain (call-site override, scoped context, recipe bindings, orchestrator advisor, category config, adapter default); infrastructure capability reports (orchestrator `/v1/recommendations`, Zen Garden tools stream, fitness verdicts).
- **Outputs:** Resolved model name routed to the correct adapter/member. `X-Zen-Resolution-Layer` response header from orchestrator proxy indicating which layer resolved the request. Recipe diagnostics in boot report.
- **Error Modes:** Recipe binding references unavailable model → falls through to next provider with diagnostic log. All providers return null → framework throws `InvalidOperationException("No model resolved for category '{Category}'")`. Recipe config section missing → no-op, chain continues without it.
- **Acceptance Criteria:** Recipe bindings in `Koan:Ai:Recipes:{Name}` resolve before orchestrator advisor. Active recipe selected via `Koan:Ai:ActiveRecipe`. Recipe bindings degrade gracefully when bound model is unavailable. Boot report shows active recipe name and per-capability resolved model. Existing resolution chain is unchanged when no recipe is configured.

**Edge Cases**

- Recipe binds `chat → llama3:8b` but model is not loaded on any stone: Recipe returns the binding unconditionally — it has no visibility into runtime availability. The adapter or orchestrator proxy handles the missing model (e.g., auto-pull if enabled, or error response). Future: Part 4 (orchestrator integration) can add availability-aware fallthrough by consulting the fitness matrix before returning a binding.
- Recipe and pin both set for same capability: Recipe is evaluated first (it sits higher in the chain). If recipe resolves, pin is never consulted. If recipe falls through, the advisor's cache (which respects pins) resolves.
- `ActiveRecipe` references a recipe name that doesn't exist in config: Treated as no recipe. Warning logged at startup.
- Recipe has no binding for a specific capability (e.g., no `Vision` key): Returns null for that capability only. Other bindings still resolve. This is normal — recipes are sparse, not total.
- Multiple recipes defined, none selected: No recipe active. Zero overhead. The `Recipes` section is documentation/versioning only until one is selected.

## Context

The Koan.AI pipeline resolves models through a priority chain documented in `IAiModelAdvisor`:

```
1. Explicit ChatOptions.Model on the call          (developer, per-request)
2. Ambient AiCategoryScope model override           (developer, per-code-block)
3. IAiModelAdvisor recommendation                   (system, from orchestrator)
4. Category configuration (Koan:Ai:{Category}:Model) (ops, in appsettings.json)
5. Source/member default model                       (framework)
6. Hardcoded fallback                                (framework)
```

This chain serves three actors: developers (levels 1-2), infrastructure (level 3), and operators (level 4). A fourth actor is missing: the **ML engineer or DevOps specialist** who understands both the application's needs and the infrastructure's capabilities.

Today, this persona must either:
- Set orchestrator pins individually via `PUT /v1/recommendations/{cap}/pin` (imperative, not versionable, not environment-scoped)
- Edit `Koan:Ai:{Category}:Model` in appsettings.json (bypasses orchestrator intelligence entirely — level 4 is below level 3)

Neither option is satisfactory. Pins are operational knobs, not declarative contracts. Category config sits below the advisor in priority, so it only activates when the advisor returns null.

The same pattern extends beyond AI. The Zen Garden ecosystem already implements intent-based resolution for databases (`zen-garden://mongodb`), storage (lazy S3 resolution via garden topology), and AI models (`recommended:chat`). Each domain follows the same structure:

```
Consumer declares INTENT  →  Pluggable PROVIDERS resolve  →  Infrastructure reports CAPABILITIES
```

A recipe is a provider in this chain — one that carries a human's opinion about which capabilities should bind to which concrete instances, expressed as a versionable, environment-scoped configuration artifact.

## Decision

### Part 1: Recipe as a Configuration Section

Recipes live in `appsettings.json` under `Koan:Ai:Recipes`. Each recipe is a named object mapping capabilities to model names. One recipe is active at a time, selected by `Koan:Ai:ActiveRecipe`.

```json
{
  "Koan": {
    "Ai": {
      "ActiveRecipe": "production-balanced",
      "Recipes": {
        "production-balanced": {
          "Chat": "qwen3.5:9b",
          "Embed": "nomic-embed-text",
          "Vision": "llava:13b",
          "Thinking": "qwq:32b",
          "Quick": "qwen3.5:1.7b"
        },
        "cost-optimized": {
          "Chat": "phi4:3.8b",
          "Embed": "nomic-embed-text",
          "Quick": "phi4:3.8b"
        },
        "dev-fast": {
          "Chat": "qwen3.5:1.7b"
        }
      }
    }
  }
}
```

**Design constraints:**
- Recipes are **sparse**: omitting a capability means "no opinion, let the next provider decide."
- Recipes are **named**: enables `git diff`, A/B testing, environment-scoped selection via `appsettings.Production.json`.
- Recipes are **static**: values are model name strings. No constraints, no conditions. Simplicity is the point — a recipe is a human's curated selection, not an algorithm.
- Recipes are **optional**: zero recipes configured = zero overhead, existing chain unchanged.

### Part 2: Resolution Chain Update

The recipe provider inserts between scope (level 2) and advisor (level 3):

```
1. Explicit ChatOptions.Model on the call            (developer)
2. Ambient AiCategoryScope model override             (developer)
3. Active recipe binding for category        ← NEW    (ML engineer / DevOps)
4. IAiModelAdvisor recommendation                     (system)
5. Category configuration (Koan:Ai:{Category}:Model)  (ops)
6. Source/member default model                         (framework)
7. Hardcoded fallback                                  (framework)
```

**Why above the advisor, not below:**
- A recipe is a human's deliberate decision. It should override automated scoring.
- The advisor is a safety net — it catches capabilities the recipe doesn't cover.
- If the recipe's bound model is unavailable, returning null lets the advisor recover automatically.

### Part 3: Implementation

**New type: `IAiRecipeProvider`** (Koan.Core.AI)

```csharp
public interface IAiRecipeProvider
{
    string? ActiveRecipeName { get; }
    string? GetModel(string category);
}
```

Single-method read interface. `GetModel` returns the recipe's model binding for a category, or null if the active recipe has no opinion.

**New type: `AiRecipeProvider`** (Koan.AI)

Reads `Koan:Ai:ActiveRecipe` and `Koan:Ai:Recipes:{name}` from `IConfiguration`. Resolves at construction time (singleton). Returns null for everything when no recipe is active.

**Router change** (`AiCategoryRouter.Resolve`)

```csharp
// Existing line:
var advisorModel = scopeModel is null ? _advisor?.GetRecommendedModel(category) : null;

// Becomes:
var recipeModel = scopeModel is null ? _recipe?.GetModel(category) : null;
var advisorModel = scopeModel is null && recipeModel is null
    ? _advisor?.GetRecommendedModel(category) : null;

var effectiveModel = scopeModel ?? recipeModel ?? advisorModel
    ?? categoryOptions?.Model ?? definition.DefaultModel;
```

Three lines changed. The recipe is just another link in the chain.

**Boot report integration:**

```
[AI] Active recipe: production-balanced
  Chat      → qwen3.5:9b        (recipe)
  Embed     → nomic-embed-text   (recipe)
  Vision    → llava:13b          (recipe)
  Thinking  → qwq:32b            (recipe)
  Quick     → qwen3.5:1.7b       (recipe)
  Ocr       → (advisor)
  Tools     → (advisor)
```

### Part 4: Orchestrator Integration (Future)

The orchestrator can evolve to serve recipes via `GET /v1/recipes/{name}`, allowing the Koan advisor to fetch recipes from infrastructure rather than only from local config. This is not part of this ADR — it requires a corresponding ORCH-NNNN decision.

When implemented, the resolution chain gains a remote recipe source:

```
3a. Local recipe (appsettings.json)                   (ops, static)
3b. Remote recipe (orchestrator /v1/recipes)           (ops, dynamic)
```

Local takes precedence (operator override). Remote provides a default when no local recipe is configured.

## Consequences

**Positive:**
- ML engineers gain a declarative, versionable, diffable way to express model selections.
- Environment-scoped recipes (`appsettings.Production.json` vs `appsettings.Development.json`) require zero code changes.
- The resolution chain gains a human-curated layer without losing automated fallback.
- Zero breaking changes — existing deployments without recipes behave identically.

**Negative:**
- One more concept to understand in the resolution chain (mitigated by boot report transparency).
- Recipe model names can drift from what's actually deployed (mitigated by graceful fallthrough + diagnostic logging).

**Risks:**
- Operators may over-rely on recipes and miss that the advisor would have selected a better model. Mitigation: boot report shows resolution source per capability. Future: dashboard shows "recipe override vs advisor recommendation" comparison.

## References

- `IAiModelAdvisor` priority chain: `Koan.Core/AI/IAiModelAdvisor.cs`
- Category router: `Koan.AI/Pipeline/AiCategoryRouter.cs`
- Zen Garden model advisor: `Koan.ZenGarden/AI/ZenGardenModelAdvisor.cs`
- Orchestrator pin system: Zen Garden `ORCH-0011-recommended-model-monikers.md`
- Orchestrator recommendation engine: Zen Garden `ORCH-0007-managed-logical-sets.md`
- Intent-capability pattern: Zen Garden `ZenGardenConnectionIntent`, Koan `IServiceDiscoveryAdapter`
