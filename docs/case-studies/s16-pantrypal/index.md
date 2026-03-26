---
type: GUIDE
domain: ai
title: "S16.PantryPal case study"
audience: [architects, developers]
status: current
last_updated: 2026-03-25
framework_version: v0.6.3
validation:
	date_last_tested: 2026-03-25
	status: verified
	scope: samples/S16.PantryPal
---

# S16.PantryPal case study

**Contract**

- **Audience**: Architects and senior engineers evaluating Koan patterns for AI-powered meal planning and vision-driven inventory workloads.
- **Inputs**: Familiarity with Koan entity-first development, AI category routing, and MCP Code Mode orchestration. Access to the `samples/S16.PantryPal` solution.
- **Outputs**: Repeatable blueprint for photo-to-inventory pipelines, natural language parsing, budget-aware meal planning, and MCP-orchestrated multi-entity workflows.
- **Error modes**: Missing vision adapter, unrecognized ingredients from photo detection, expired-item edge cases in meal planning, or MCP Code Mode timeouts. Each section below calls out fallback behavior.
- **Success criteria**: Teams can replay the sample end-to-end, understand the split-stack architecture (API + MCP Host), and adapt the pattern to their own planning or inventory workloads.

**Edge cases to watch**

1. Low-quality pantry photos may produce false positives in object detection; the confirmation step lets users correct bounding-box results before committing to inventory.
2. Natural language date parsing ("next Friday", "in 3 days") depends on locale; verify `CultureInfo` defaults match your deployment target.
3. Budget and nutrition constraints can conflict; the planner returns a ranked list with trade-off explanations rather than silently dropping constraints.
4. MCP Code Mode scripts that span multiple entities (e.g., plan + shopping list + pantry deductions) must execute atomically; partial failures roll back via `TrackedOperations`.
5. Duplicate detection across photo uploads and manual entry relies on fuzzy name matching with category awareness; tune the similarity threshold in `PantryPal:Detection:FuzzyThreshold`.

## Narrative

S16.PantryPal demonstrates how Koan composes vision AI, natural language parsing, and multi-entity orchestration into an intelligent meal-planning loop. The sample uses a split-stack architecture: a REST API for standard CRUD and a dedicated MCP Host (HTTP SSE) for complex AI-driven workflows.

| Phase                | Highlights                                                                                                      | References                                                                     |
| -------------------- | --------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| **Vision**           | Photo upload, AI object detection with bounding-box confirmation, duplicate detection against existing inventory | [`S16-0001`](../../decisions/S16-0001-pantrypal-ai-powered-meal-planning.md)   |
| **NLP Parsing**      | Natural language input ("5 lbs chicken, expires Friday"), flexible date parsing, smart category defaults         | [`S16-0001`](../../decisions/S16-0001-pantrypal-ai-powered-meal-planning.md)   |
| **Meal Planning**    | Recipe suggestions from pantry + user preferences, multi-day planning, waste reduction via expiry-aware ranking  | [`S16-0002`](../../decisions/S16-0002-pantrypal-meal-planning-optimization.md) |
| **MCP Orchestration** | Code Mode scripts for complex workflows, multi-entity aggregation, conditional logic across plan lifecycle       | [`AI-0014`](../../decisions/AI-0014-mcp-code-mode-orchestration.md)            |

Core entities: `Recipe`, `PantryItem`, `PantryPhoto`, `MealPlan`, `ShoppingList`, `UserProfile` — all follow standard entity-first patterns with automatic GUID v7 generation and category-driven AI routing ([AI-0021](../../decisions/AI-0021-category-driven-ai-with-convention-defaults.md)).

## Quick start

1. `./start.bat` from `samples/S16.PantryPal` to launch API, MCP Host, MongoDB, and Ollama.
2. Navigate to the client at `http://localhost:5116` and upload a pantry photo from `samples/S16.PantryPal/sample-data`.
3. Confirm detected items in the bounding-box review screen, then switch to the meal planner to generate a weekly plan.
4. Attach an MCP client to the HTTP SSE endpoint advertised in the boot report to execute Code Mode meal-planning scripts.

## When to extend

- **Barcode scanning**: Add a barcode-detection category adapter alongside the vision adapter; the entity pipeline remains unchanged.
- **Household sharing**: Layer multi-tenant `UserProfile` scoping to share pantries across household members with independent preference profiles.
- **Grocery API integration**: Inject a `IShoppingListFulfiller` to push generated shopping lists to external grocery delivery APIs.

## Related reading

- [`Guides: Data modeling`](../../guides/data-modeling.md)
- [`Reference: AI module`](../../reference/ai/index.md)
- [`AI-0021: Category-driven AI with convention defaults`](../../decisions/AI-0021-category-driven-ai-with-convention-defaults.md)
- [`AI-0014: MCP Code Mode orchestration`](../../decisions/AI-0014-mcp-code-mode-orchestration.md)
