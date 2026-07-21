# Executive Summary + Architecture (Koan → .NET 10)

**Thesis:** Adopt a selective set of .NET 10 features that strengthen Koan’s “Reference = Intent” design, reduce glue code, and improve performance/ops—without compromising Koan’s storage-agnostic, entity-first approach or global polymorphism needs. fileciteturn0file16

---

## Top Outcomes (90-day horizon)

1. **OpenAPI 3.1 by default** via `Microsoft.AspNetCore.OpenApi`, with deprecation of legacy `WithOpenApi` usage. Align docs/UIs and keep swagger UI optional. (Group A) citeturn6search0turn6search1turn6search5  
2. **First-class SSE** streaming endpoints (typed and string), for Minimal APIs and MVC controllers—used by Koan MCP and AI streaming. (Group A) citeturn1search0turn1search6turn1search2  
3. **JSON strategy**: Keep **Newtonsoft.Json** as Koan’s default for open/runtime polymorphism; enable **STJ strict mode** only where types are closed (Minimal APIs, internal DTOs). (Group A) citeturn3search0turn3search6turn0search4  
4. **CLI & build modernization**: Embrace .NET 10 SDK features—**one‑shot tool exec**, `dnx` script, **package pruning**, native tab completions—to improve Koan CLI DX and CI. (Group A) citeturn9view0  
5. **Container/AOT hygiene**: Document blessed lanes (Windows, Docker/K8s, GitHub Actions, Azure DevOps) with **.NET publish-as-container** and AOT notes. (Group A) citeturn2search3turn2search12turn2search5  
6. **Source-generated registries** for AOT friendliness and faster boot (optional behind flag). (Group B) fileciteturn0file16  
7. **WebSocketStream adapters** for realtime pipelines where bidirectional channels beat SSE. (Group B) citeturn0search3turn0search10  
8. **Unify Koan.AI over `Microsoft.Extensions.AI`** while keeping Koan providers (Ollama, etc.). (Group B) citeturn5search0turn5search1  
9. **PQC toggles** (opt‑in) for future‑proof crypto on supported platforms. (Group B) citeturn7search0turn7search4  
10. **Agent runway**: Prototype **Microsoft Agent Framework** integration as an optional kit under `Koan.AI.Agents` (MCP-aware). (Group C) citeturn8search0turn8search3

---

## ROI vs Effort (T‑shirt sizing)

| Group | Items | Value | Effort | Why now |
|------:|------:|:-----:|:------:|---------|
| **A** | A1–A6 | **High** | **S–M** | Moves DX/observability forward with minimal code churn. |
| **B** | B1–B4 | High | M–L | Structural wins (AOT, realtime, AI unification, security). |
| **C** | C1–C2 | Med | L | Strategic runway (agents) & deeper vector wins. |

> Koan’s provider-agnostic design, auto-registrars, and capability detection make these changes low‑risk and coherent with the “single pattern scales” principle. fileciteturn0file16

---

## Architecture Notes (alignment with Koan)

- **Auto‑enable by reference**: New modules follow `IKoanAutoRegistrar` so “add reference ⇒ capability appears in boot report”. Works for `Koan.Web.OpenApi`, `Koan.Web.Sse`, `Koan.Web.JsonPatch.STJ`, `Koan.Cli`. fileciteturn0file16
- **Provider transparency preserved**: No EF dependency added. Data remains polyglot (Postgres, Mongo, Sqlite, Weaviate, RabbitMQ). fileciteturn0file14
- **JSON stance**: Newtonsoft remains for global polymorphism; STJ only for closed/known DTOs (Minimal APIs, internal contracts) and **strict duplicate-property rejection** where enabled. citeturn3search0turn0search4
- **Streaming**: SSE becomes the default server→client push; WebSockets added via `WebSocketStream` when bidirectional needed. citeturn1search0turn0search3

See Module Ledger to plan safe blast radius for breakages (most Web/* leaf modules are low‑risk). fileciteturn0file15
