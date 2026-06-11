# Koan Framework Samples

**The samples are the curriculum**: each rung teaches a bounded set of concepts; the flagships
are dogfood applications that drive framework evolution (aspirational reading, not tutorials).

> Status below is truthful as of 2026-06: ✅ = in `Koan.sln`, builds, CI-protected ·
> 🛠 dogfood = large real app, maintained but not a tutorial · ⚠ = on disk but currently broken
> or not in the solution (being consolidated; see `docs/assessment/`). The old
> [CATALOG.md](CATALOG.md) is superseded by this page pending regeneration.

## The learning ladder (do these in order)

| Rung | Sample | Teaches | Time | Status |
|---|---|---|---|---|
| 1 | **S0.ConsoleJsonRepo** | Minimal bootstrap; entity statics in a console app | 5 min | ✅ |
| 2 | **S1.Web** | REST CRUD via `EntityController<T>`, relationships (`[Parent]`), pagination, `[Cacheable]` | 30 min | ✅ |
| 3 | **S10.DevPortal** | Live multi-provider switching (Mongo ⇄ Postgres ⇄ SQLite), capability detection, bulk ops | 20 min | ✅ |
| 4 | **S14.AdapterBench** | Cross-adapter benchmarking; **entity-first background jobs** (`IKoanJob<T>`, progress, durable ledger) | 20 min | ✅ |

After the ladder, pick by interest:

| Sample | What it is | Status |
|---|---|---|
| **S5.Recs** | AI recommendation engine: Mongo + Weaviate + Ollama, `[Embedding]` pipeline, partitioned imports, auth, scheduling | ✅ 🛠 dogfood |
| **S16.PantryPal** (API + MCP host) | Vision AI meal planning; `[McpEntity]` agent tools over HTTP/SSE; MCP Code Mode | ✅ 🛠 dogfood |
| **S18.Prism** | Personal knowledge intelligence; exercises the AI pillar end-to-end | ✅ 🛠 dogfood (spec-led; no README yet) |
| **S8.Canon** (Api + Shared) | Canon runtime pipelines (`CanonEntity<T>`, pipeline contributors) | ✅ (root project excluded; use Api) |
| **g1c1.GardenCoop** (guides/) | Narrative chapter-style guide; the only NativeAOT-publish dogfood | ✅ |
| **S3.Mq.Sample** | RabbitMQ messaging skeleton | ✅ builds, minimal — being rebuilt as a proper messaging rung |
| **S7.Meridian** | Document-intelligence flagship (16k LOC) | ⚠ builds (recently restored) but outside `Koan.sln` |
| **S6.SnapVault** | Photo manager (media + storage + AI) | ⚠ broken (dependency pin); consolidation candidate |
| **S8.PolyglotShop** | ServiceMesh showcase | ⚠ broken (references retired experimental pillars) |

Known gaps the consolidation is addressing: no dedicated **messaging**, **jobs**, or **cache**
tutorial rungs yet (today they're embedded in S3/S14/S1); several ghost directories from
archived samples are pending deletion. Don't trust directory listings — trust this table and
`Koan.sln`.

## Running a sample

```bash
# Anything in Koan.sln:
dotnet run --project samples/S1.Web

# Samples with container dependencies ship a start.bat (preferred over docker compose by hand):
cd samples/S5.Recs && ./start.bat
```

Each sample prints a **boot report** at startup — discovered modules, adapter elections, boot
phases. Read it; it is the framework's self-description and your first debugging surface.

## Sample principles

1. **Domain-focused** — real applications, not FooService demos
2. **Entity-first** — `Entity<T>` patterns; no manual repositories
3. **Reference = Intent** — capabilities arrive by package reference; Program.cs stays minimal
   (the canonical form is 4 lines — extra incantations in older samples are being removed)
4. **In the solution = alive** — every kept sample lives in `Koan.sln` and rides CI; anything
   outside the solution is rotting by definition
5. **Concept-budgeted** — each README states what new concepts the sample introduces

## Contributing a sample

Follow S5.Recs' README style (tutorial narrative, *why* over *what*), one-command run
(`start.bat`), entity-first patterns only, and add the project to `Koan.sln` in the same PR.

- Port allocations: `docs/decisions/OPS-0014-samples-port-allocation.md`
- Archive policy & history: [archive/ARCHIVED.md](archive/ARCHIVED.md)
