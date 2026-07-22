---
type: SPEC
domain: framework
title: "R13-15 - Promote the AI runtime and local providers"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: in-progress
  scope: AI runtime/contracts, Ollama, LM Studio, ONNX, package, consumer, product, and API evidence
---

# R13-15 — Promote the AI runtime and local providers

## Architecture checkpoint

**Task:** Promote the provider-neutral AI runtime, its inert contracts, and the intended Ollama,
LM Studio, and ONNX providers to the supported 0.20 surface without pulling unrelated AI projections,
agents, evaluation, training, or orchestration into the slice.

**Application intent:** An application installs the AI runtime and one local provider, keeps ordinary
`AddKoan()`, and asks `Client` for chat or embeddings without constructing a provider client, adapter,
router, registry, or model session.

**Public expression:** The normal endpoint-backed path is the runtime plus one provider package:

```powershell
dotnet add package Sylin.Koan.AI
dotnet add package Sylin.Koan.AI.Connector.Ollama
# or Sylin.Koan.AI.Connector.LMStudio
```

```csharp
using Koan.AI;
using Koan.Core;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddKoan();
using var app = builder.Build();
await app.StartAsync();

Console.WriteLine(await Client.Chat("Summarize today's orders."));
```

Ollama or LM Studio must be reachable at its conventional development endpoint or configured exact
endpoint, and a suitable model must be installed. The in-process embedding alternative replaces the
provider package with `Sylin.Koan.AI.Connector.Onnx`, supplies local model/vocabulary paths, and calls
`Client.Embed(...)`; it requires no service process or network.

**Guarantee/correction:** `AddKoan()` compiles one provider topology, activates each provider once with
DI-owned lifetime, preserves explicit placement over automatic discovery, routes an operation only to
a source advertising that capability, and keeps provider/model/protocol failures visible. A referenced
but undiscovered automatic provider is honestly inactive. Conflicting endpoint forms, an unresolved
explicit service intent, a missing ONNX artifact, no eligible source, or an unsupported operation fails
with the existing corrective boundary; Koan does not silently fall back, launch desktop software,
download ONNX assets, or pretend that a model exists.

**Complete intent surface:** Runtime package; one provider package; `AddKoan()`; a `Client` operation;
optional existing source/model scope when multiple providers express a real routing decision; exact
endpoint/authentication configuration when conventions do not locate the service; and either a ready
Ollama/LM Studio runtime with a compatible model or local ONNX model/vocabulary files. There are no
additional registration calls, provider lists, adapters, repositories, or HTTP projections.

**Public concepts:** `Client` is the application operation language; `Client.Scope` expresses a genuine
multi-provider placement decision; existing provider options express endpoint, authentication, model,
and ONNX artifact intent; contracts carry provider-neutral requests/results and remain inert by
themselves. No new public concept is required.

**Docs read:**

- `docs/engineering/index.md` — requires focused owner evidence, package hygiene, centralized stable
  identifiers, and proportionate validation; governing.
- `docs/architecture/principles.md` — assigns composition law to Core, AI meaning/routing to the pillar,
  protocol mechanics to adapters, and business operations to the application; governing.
- `docs/decisions/ARCH-0120-terminal-package-maturity.md` — names this exact five-owner value family and
  requires runtime/protocol/consumer evidence without a universal admission layer; governing.
- `docs/reference/ai/index.md` — freezes `Client`, provider availability, source/model routing, failure,
  and provider limits; directly applicable, with inline-endpoint examples to correct to controller-free
  console composition rather than teach a forbidden HTTP shortcut.
- `docs/initiatives/koan-v1/work-items/R13-terminal-package-maturity.md` — places the AI runtime and these
  three providers immediately after Vector/Search; directly applicable.

**Code read:**

- `Client.cs` — owns the provider-neutral business facade and host resolution; keep.
- `KoanAiPipeline.cs` and `AiCategoryRouter.cs` — own capability-aware operation routing and corrective
  source/adapter/model selection; keep at the AI pillar.
- `AiProviderPlanInitializer.cs` — owns compile-once activation, id agreement, source registration, and
  DI lifetime; keep as the shared runtime chokepoint.
- Ollama and LM Studio adapter contributors/adapters — own endpoint precedence, discovery, native HTTP
  dialects, readiness, and model behavior; keep provider-specific.
- `OnnxAdapterContributor.cs` and `OnnxEmbeddingAdapter.cs` — own local artifact validation, session
  lifetime, tokenizer/pooling, and in-process source publication; keep provider-specific while moving
  stable identifiers to its existing project boundary.

**Reusing:** Existing `Client`, contracts, compiled AI provider plan, category router, source registry,
typed provider options, Core discovery, DI lifetime, startup/health reporting, provider activation tests,
AI unit suite, deterministic HTTP handlers, real committed ONNX model, package compiler, API guard, lean
PR gate, and main publisher.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| ONNX provider/source/configuration identifiers | `src/Connectors/AI/Onnx/Infrastructure/Constants.cs` | Centralize stable adapter identity, source/member names, section, policy, origin, and log action inside the owning connector without changing behavior. |
| Ollama deterministic wire specs | `tests/Suites/AI/Unit/Koan.Tests.AI.Unit/Specs/Adapters/OllamaAdapter.Spec.cs` | Close the only provider-specific protocol evidence gap using the existing unit owner and no service/model download. |
| R13-15 evidence card | This file | Freeze the honest five-owner claim, boundaries, and public results without another promotion framework. |

**Coalescence:** Closest pattern: the existing AI runtime plus Ollama/LM Studio contributors. Core owns
generic semantic contribution ordering; `Koan.AI` owns provider-plan compilation and routing; each
connector owns deployment and protocol differences. Disposition: keep this capability-family split;
absorb only ONNX stable literals into connector-scoped constants; add the missing Ollama wire delta to
the existing AI unit owner. A wider universal provider base is wrong because endpoint discovery, model
readiness, protocol, and in-process lifetime differ. A narrower provider-owned router would duplicate
AI meaning. The contracts package moves from the prompt/projection claim to the runtime foundation
because it is the runtime's inert public dependency; no runtime path is superseded or deleted.

**Ergonomics:** Installation, IntelliSense, and application code stay centered on `Client.Chat`,
`Client.Embed`, and optional `Client.Scope`. Provider options appear only for real placement/model/auth
decisions. Plan contributors, activators, registries, adapters, source members, HTTP dialects, tokenizer,
and ONNX sessions remain invisible. Current documentation stops teaching inline HTTP endpoints for a
capability that does not require HTTP.

**Constraints satisfied:**

- No Entity data access or large-data path is introduced.
- No HTTP endpoint is added; corrected examples use a normal host and console call.
- Stable ONNX literals move to adapter-scoped constants; tunables remain typed options.
- Existing README/TECHNICAL companions and generated product truth receive the support claim/limits.
- Existing runtime/provider tests and deterministic/native boundaries provide evidence; no generic
  certification or admission infrastructure is added.

**Risks:** Ollama and LM Studio model execution is not deterministic without large external downloads;
their meaningful boundary is therefore exact native HTTP request/stream/embedding behavior plus normal
activation, while ONNX executes the committed real model in process. The clean package consumer must
exercise both deterministic service dialects and the real ONNX model so package composition is not
mistaken for inference proof. If any provider cannot meet that boundary, split the provider claim
rather than weakening it.

## Evidence boundary

1. Run the existing AI unit owner and provider-activation owner; add only the missing Ollama native
   wire delta alongside the existing LM Studio protocol specs.
2. Run only the three explicit ONNX bootstrap specs against the committed real model; do not run the
   complete infrastructure lane.
3. Pack the five owners (`AI.Contracts`, `AI`, and three providers) with `PublicRelease=true`; inspect
   their supported Koan dependency bands and ONNX native runtime assets.
4. Restore/build/run one clean external staged-package consumer in a fresh cache. Exercise Ollama and
   LM Studio through deterministic native protocol services and ONNX through the real local model,
   all through normal `AddKoan()` and `Client` operations.
5. Compile product truth, run API posture and lean no-tests coherence, publish through `main`, then
   rerun the unchanged consumer from NuGet.org-only packages.
6. Do not run unrelated AI projections, agents, evaluation, training, orchestration, external Vector,
   or whole-framework certification.

## Focused evidence — 2026-07-22

- existing AI unit owner: 163/163 passed, including the new three-spec Ollama native generate,
  NDJSON streaming, and ordered embeddings boundary;
- existing provider-activation owner: 2/2 passed through normal compiled `AddKoan()` activation;
- the three explicit ONNX bootstrap specs passed against the committed real
  `all-MiniLM-L6-v2` model: direct adapter semantics, end-to-end `Client.Embed`, and corrective
  missing-model boot failure. The spec now pins Communication's four lanes to the built-in
  in-process provider so unrelated Redis discovery cannot become its framework-broadcast owner;
- five `PublicRelease=true` packs produced exact staged `0.20.0` package and symbol artifacts.
  Every Koan dependency is bounded to `[0.20.x, 0.21.0)`; the ONNX connector depends on
  `Microsoft.ML.OnnxRuntime 1.27.1`, whose transitive package owns the platform-native assets;
- one clean external package-only consumer restored the five staged owners into a fresh cache and
  built with zero warnings/errors. Normal `AddKoan()` plus `Client.Scope`/`Client` traversed native
  Ollama `/api/generate`, native LM Studio `/v1/chat/completions`, and the real in-process ONNX model,
  emitting `AI|PACKAGE-CONSUMER|ADDKOAN|CLIENT|OLLAMA-NATIVE|LMSTUDIO-NATIVE|ONNX-REAL-MODEL|PASS`;
- generated product truth is current at 41 claims / 93 packages. API posture is 62/67 configured,
  with exactly these five allowed first-publication floors pending and three content-only owners;
- lean no-tests coherence passed the Release build, the two tracked sample composition lockfiles
  refreshed from prior AI versions to exact `0.20`, documentation truth/lint, diff-scoped code
  validation, skills lint, and blueprint lint. No unrelated tests, containers, model downloads, or
  certification suite ran.
