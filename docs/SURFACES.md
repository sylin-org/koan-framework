# Surface Ledger

> **Rotation contract.** Before the lane leaves this repo or surface: tag; CI green;
> a tripwire exists for every surface the departing work was exercising; status
> endpoints tell the truth; this ledger is updated. Leave a guard at the door when
> you leave the room.

This is the mechanical memory behind the maintainer's serial-lane model (Epic E02).
Each row records a pillar/plane-level **surface**, **who exercises it**, **when it was
last exercised**, and **what guard protects it**. When the focus lane rotates away,
unguarded surfaces rot silently — the ledger turns that from a discovered fiction into
a known risk.

**Columns.** `Exercised by` ∈ {a named in-repo test/sample suite, `zen-garden`, `koan`,
`private downstream solution`, `none`}. `Guard` ∈ {test/CI job name, `none`}.
`Last exercised` is a real date (`YYYY-MM-DD`) or `unknown since <date>` — never a
guessed "works".

**Honesty notes for this repo.**
- **Dates** are seeded from `git log -1 --format=%as <paths>`. The uniform `2026-06-11`
  stamps come from squash-merge `ee08fa53` (`Dev #90`, 1507 files), i.e. *last landed in
  tree* — so dormant/untested pillars are rendered `unknown since 2026-06-11` per the
  honesty rule, not "exercised on 2026-06-11".
- **Guards marked `(local)`** run only in the local green-ratchet. KOAN's general
  build/test CI is **disabled by design** (`ci.yml`/`validate-main-pr.yml` are noop
  placeholders; the only active workflow, `release-on-main.yml`, is build-only and runs
  no tests). A standalone `surfaces.yml` lints *this ledger*; it does not run the suites.

| Surface | Exercised by | Last exercised | Guard | Notes |
|---|---|---|---|---|
| Data inner ring (Abstractions/Core/Relational + filter pipeline) | Data suites (FilterConvergence oracle, DATA-0100) | 2026-06-11 | Data suites (local) | Strongest pillar; canon-grade tests |
| Data connectors (10 under Connectors/Data) | AdapterSurface suites | 2026-06-11 | connector suites (local; 39/87 not in sln) | Redis FilterSupport.Full is correct (in-memory-eval adapter, ARCH-0084) — scan-backed cost is documented, not a lie; native key TTL added (DATA-0101; Redis caps+TTL specs 2026-06-17) |
| Web nucleus (EntityController to hook pipeline) | Web AdapterSurface x8 | 2026-06-11 | Web suites (local) | Load-bearing; MCP executes through it |
| Cache (L1/L2 + coherence) | Cache CrossEngine oracle + KOAN0001 analyzer | 2026-06-11 | Cache suites + analyzer (local) | Reference pillar (ARCH-0075/0078) |
| Observability (OTel Reference=Intent) | Koan.Observability.Tests | 2026-06-17 | ObservabilityReferenceIntentSpec (local, mutation-checked) | Extracted from Koan.Core into an opt-in leaf package (ARCH-0088); registrar wires OTel on reference. OTel no longer hard-pulled by any framework package |
| Jobs (JOBS-0005, 5-tier) | Jobs 5-tier TestKit | 2026-06-17 | Jobs TestKit (local) — lane-fairness + starvation + health specs | JOBS-0008 lane-fair dispatch: per-node cross-lane WFQ + per-lane indexed head seek replaces the global-FIFO claim (`fed_lane_does_not_starve_a_backlog_lane`, `lane_weights_*`, `jobs_health_*` green on in-memory 74/74 + SQLite 76/76). Durable cross-node `LaneCursor` REJECTED (per-claim shared-row CAS = write-contention hotspot, SQLite 'database is locked'; per-node WFQ is starvation-free globally). `JobsHealthContributor` = cheap GLOBAL probe (oldest-queued-age tripwire), not per-lane scan. JOBS-0007 head-of-line subsumed by the per-lane seek |
| Relational schema indexes (composite [Index] groups) | Jobs SQLite HighVolumeScanShapeSpec | 2026-06-17 | Jobs Adapter.Sqlite suite (local) | JOBS-0008 fix: `RelationalSchemaOrchestrator` now creates declared per-column AND composite indexes at table-create time (latent gap: fresh relational tables previously had no secondary indexes). Mongo index field-name (PascalCase→camelCase) also fixed |
| Vector and search | VectorAdapterSurface matrix | 2026-06-11 | Vector surface matrix (local) | PGVector does not compile (parked on a branch) |
| AI core (Contracts to AI to Data.AI + Ollama/LMStudio + AI.Web) | 5 samples + 2 services + tests | 2026-06-11 | AI core tests (local) | Settled, consumed |
| AI vertical (Agents/Compute/Eval/Training/Orchestration) | none | unknown since 2026-06-11 | none | Born 2026-05-16; Training/Eval only throw; ~0 consumers |
| MCP server | MCP suites + AS-edge specs + Streamable e2e | 2026-06-20 | MCP suites + Koan.Mcp.Streamable.IntegrationTests + Koan.Web.Auth.Server.IntegrationTests (local) | Real and consumed (6.4k LOC). AI-0037: the HTTP edge is now **Streamable HTTP** (single `{baseRoute}` POST/GET/DELETE, spec 2025-06-18) by default — one dispatch+session core; the legacy `/sse`+`/rpc` is a deprecated byte-faithful shim (opt-in `EnableLegacySseTransport`), pinned by the golden `LegacyHttpSseWireSpec`. SEC-0006: the edge is an OAuth resource server (Koan.bearer ES256, RFC 9728 WWW-Authenticate, RFC 8707 audience via McpEdgeAuth/McpResourceIdentity), guarded by McpAuthRampSpec/McpConfiguredResourceSpec + StreamableAuthSpec (same-principal crux). |
| Security.Trust (SEC-0001 / KSVID + SEC-0006 ES256 tier) | unit + integration suites | 2026-06-20 | Security.Trust suites (EcdsaIssuerTests/KoanBearerSchemeTests) (local) | Best-engineered small project; SEC-0006 added the real ES256 asymmetric issuer tier (EcdsaIssuer/IAsymmetricIssuer, persisted+rotating JWKS keys, fail-closed boot guard) + the Koan.bearer scheme validating BOTH issuer tiers |
| Web.Auth flows | Koan.Web.Auth integration + e2e | 2026-06-20 | Koan.Web.Auth.Integration.Tests + Koan.Web.Auth.Tests (local) | WEB-0071: maintained OAuth2/OIDC handlers via dynamic scheme registration — the OIDC-501 was fixed; real-Kestrel OAuth2/OIDC e2e (id_token validation) |
| Web.Auth.Server (OAuth 2.1 AS, SEC-0006) | Koan.Web.Auth.Server.IntegrationTests | 2026-06-20 | Koan.Web.Auth.Server.IntegrationTests (43 specs, local) | Embedded OAuth 2.1 AS — the MCP auth on-ramp: /oauth authorize+token+device+register, RFC 8414/9728/8707 discovery+audience, rotating refresh w/ reuse-detection over AgentGrant, ES256 issuer/JWKS; 5 mutation-verified cruxes |
| ZenGarden bridge (Koan.ZenGarden.Core + mainline connector refs) | zen-garden + private downstream solution | unknown since 2026-06-11 | none | 5 mainline refs slated for E06 inversion; S3 presign throws without Moss; AI.Connector.ZenGarden not in sln |
| Messaging (Messaging.Core) | none | unknown since 2026-06-11 | none | Zero broker-backed tests (ARCH-0079 violation); pre-redesign idiom |
| Scheduling | none (2 trivial consumers) | unknown since 2026-06-11 | none | OPS-0050 fragment; subsumed by JOBS-0005; zero tests |
| Orchestration (CLI/Generators) | none | unknown since 2026-06-11 | none | Condemned by ARCH-0077 (retire for Aspire); tests deleted |
| Media / Storage | Media and Storage suites (partial) | 2026-06-11 | none for replication/WAL (~2.2k LOC, 0 tests) | Clean Media/Storage layering; S3 connector ZenGarden-entangled |

---

*Seeded by Epic E02 (2026-06-13). Every lane that touches a surface above updates its
row — `Last exercised` to today, `Guard` to the tripwire it left — before it leaves.*
