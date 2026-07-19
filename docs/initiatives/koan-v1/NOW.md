---
type: GUIDE
domain: framework
title: "Koan V1 Reorganization Current Handoff"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: tested
  scope: R11-05 ZenGarden graduation and focused runtime/consumer/package proof
---

# Koan V1 reorganization current handoff

Replace this file at every handoff. It is a restart point, not a diary.

## Outcome so far

- R00 through R07, R09, and R10 pass. R08 is locally prepared but publication remains deliberately gated.
- R11-01 through R11-06 pass. Every one of the 93 active packages has a terminal topology disposition and objective
  package-quality finding count is zero. R11-07 is running the single complete local release-certification boundary.
- Foundation, package identity, templates, Storage, Cache, Redis, relational/Data providers, Vector, AI, MCP, Web,
  Media, OpenGraph, Identity, Tenancy, Classification, Canon, RabbitMQ, Web request context/Data Access, Data
  Backup/SoftDelete, Jobs, Observability, Security Trust, Testing, Web Admin, and ZenGarden completed their current
  R11 family work.
- The bespoke Orchestration CLI and Aspire families are shelved beyond V1 under `shelved/`, outside `Koan.sln`, active
  package discovery, and the release graph. Their retained source is not a V1 package promise.
- Evaluated truth is 93 packages and 26 claims. Generated package quality is 0 repair-required, 0 review-required,
  93 structurally ready, and zero findings.

The accepted architecture remains business intent first: fewer meaningful moving parts, Entity-first application
language, references express capability intent, `AddKoan()` compiles host-owned decisions, pillars own meaning and
runtime chokepoints, adapters own mechanics, and applications own business rules. Cross-module contracts survive only
when genuinely inert and independently consumed.

## Most recent completed slice: package rendering and clean-consumer proof

The last 25 generated findings were concentrated in seven already-terminal AI packages: Agents, Eval, Models,
Orchestration, Review, Data.AI, and HuggingFace. Their package pages now use exact IDs/install expressions, compile
against current public signatures, state automatic activation and deliberate application registration separately,
name provider/Data/Vector requirements, and identify corrective failures and non-guarantees. HuggingFace is accurately
presented as Hub model discovery/download rather than inference; Review accurately leaves Entity persistence,
authorization, concurrency, and audit to the application.

A single clean net10.0 temporary consumer compiles every advertised surface warning-as-error. All seven build and pack
in Release; exact artifacts contain owned rendering/assets and expected dependencies, and current vulnerability audits
are clean. Generated quality is now 93/93 structurally ready with zero findings. Strict API/full-site docs and the
233-file public truth gate pass; broad lint has zero errors and 1,624 pre-existing non-gating warnings. No production
code or broad AI behavior suite changed or ran.

## Previous completed slice: ZenGarden

`Sylin.Koan.ZenGarden` remains one functional runtime package. Reference plus the application's existing `AddKoan()`
activates the DI-owned client, layered Moss/Koi/UDP discovery, tools catalog/events, capability wishes,
connection-intent initialization, persisted roster, and optional AI model advice. The application language is three
named surfaces after host start: `ZenGarden.Offering`, `.Storage`, and `.Capability`; durable reactions use a standard
hosted service.

`Sylin.Koan.ZenGarden.Contracts` remains a genuine dependency-free boundary. Mongo, Weaviate, Ollama, and S3 reference
its client/intent/resolution vocabulary without acquiring the runtime engine. R10/R11's earlier contract isolation was
preserved rather than repeated or reversed.

The runtime is smaller and more truthful. The ownerless endpoint manager/circuit and unproved storage-CDC second
client are deleted. Manual process-global client configuration, runtime-checked generic event dispatch, and duplicate
facade model advice are gone. Concrete client, Koi handler/topology records, and constants are internal mechanics.
Standard options validation rejects invalid numeric/duration settings. Provenance says only whether a cache path is
configured or automatic and never publishes its value or resolved host path.

The package README now has exact identity/install instructions, a real running-host lifecycle example, the three
surfaces, corrections, and non-capabilities. Technical docs use the current Koi discover/subscribe routes. Runtime is
now structurally ready; Contracts remains structurally ready.

## Focused proof for ZenGarden

- ZenGarden suite: 82 passed / 7 visible environment skips / 0 failed (89 total). Three options-validation tests are
  new. The previous 86/86 baseline included seven silent returns; absence is now reported honestly.
- Mongo, Weaviate, Ollama, and S3 production connector Release builds: zero warnings/errors.
- Semantic activation: 2/2, proving S3's Contracts dependency is inert and explicit Weaviate intent cannot fall back
  autonomously.
- The focused Mongo ZenGarden filter is blocked at compilation by an unrelated pre-existing missing
  `FieldTransformRoundTrip` symbol in its broad test host. Production Mongo builds and ZenGarden-owned intent/live
  cells pass; the already-passed Data transform slice was not reopened.
- Runtime and Contracts Release packs produce nupkg/snupkg under untracked `tmp/r11-zengarden`; archive inspection
  confirms exact IDs, DLL/XML, README, icon, build-transitive props, symbols, provenance, and expected dependency
  boundaries. Current direct/transitive vulnerability audits are clean for both projects.
- Generated truth: 93 active packages / 26 claims; quality 1 repair / 6 review / 86 structurally ready.
- Strict API/full-site DocFX passes; public documentation truth passes 226 current files / 42 navigation targets;
  broad docs lint has zero errors and 1,621 existing non-gating warnings.
- No live Garden requirement, unrelated completed-family suite, full release ratchet, private downstream inspection,
  push, publication, tag, release, deployment, or remote mutation occurred. `tmp/` remains untracked.

## Previous completed slices that must not be reopened

- Web Admin is one authenticated, Development-only, read-only projection over canonical provenance/health/runtime.
  Its false LaunchKit/Compose/Aspire generator, raw manifest, service-mesh/style/control-plane promises, manual
  registration, and weakening security options are gone. Real-host proof is 12/12.
- Testing remains three meaningful packages: xUnit-free Hosting, lightweight conformance, and optional heavy provider
  fixtures. Arbitrary application defects no longer become infrastructure skips.
- Security Trust is one ES256 issuer/key/verifier path. Direct Trust is ephemeral/same-host; Auth Server owns persisted
  rotation and MCP owns exact resource audience.
- Observability is one optional standard OpenTelemetry leaf over the `Koan.*` trace/meter boundary, with no manual
  callback, temporary provider, fixed source registry, or Koan exporter abstraction.
- Jobs remains one functional capability with internal runtime implementations, automatic Data CAS claims, explicit
  capability requirements, and neutral Communication wake signals.
- Canon's R10-11 automatic contributor discovery, functional/Web split, four-line CustomerCanon host, and
  failed/parked non-persistence remain intact.
- Web context-aware behavior uses ordered `IWebContextContributor`s at the request chokepoint. Scoped link evidence,
  tenant, principal, capability scopes, rejection, and read predicates are contributed centrally; arbitrary durable
  filter carriage and model decoration are not restored.
- CLI and Aspire remain shelved beyond V1. Standard AppHost/Compose/Docker/Podman/Kubernetes tooling owns topology.

## Current repository state

- Workspace: `F:\Files\repo\github\sylin-org\koan-framework`.
- Branch: `dev`; the bounded ZenGarden graduation commit is expected to be current local HEAD. Verify exact HEAD and
  ahead/behind counts before continuing.
- `tmp/` is untracked scratch/evaluator/artifact material and must never be staged.
- No process started by the focused slice is expected to remain running.

## Resume here

1. Verify `git status`, HEAD, and the completion evidence in
   [R11-05](work-items/r11/R11-05-package-family-graduation.md).
2. Open R11-07 from the parent acceptance and validation-economy contract. It is the one complete active-package,
   template, FirstUse, GoldenJourney, public-docs, and release-certification boundary; do not split it into repeated
   family work or mutate public/remote state.
3. Reuse R11-05/R11-06 focused evidence for diagnosis, but run the complete ratchet required by R11-07 and record every
   failure/correction. Publication remains separately authorized and out of scope.

## Remaining temporary dispositions

None. Every active package has a terminal R11-02 disposition.

## Do not redo

- Do not reopen R10-11 Canon or rebuild CustomerCanon.
- Do not recreate Data Access, ambient subjects, `[AccessScoped]`, durable arbitrary-filter carriage, Backup HTTP
  control planes, Web Extensions partition-backed soft delete, or Jobs' removed public runtime/claim branches.
- Do not restore Observability's manual registration/fixed source list, Trust's shared secret/parallel issuer stack,
  Testing's catch-all skips, or Web Admin's false control plane/security bypasses.
- Do not merge ZenGarden Contracts into runtime, restore the endpoint manager/storage CDC/manual Configure/generic
  events, or expose Koi/concrete client mechanics without new independent-consumer evidence.
- Do not move the shelved CLI or Aspire families into `src/`/`Koan.sln` or treat retained source as V1 scope.
- Do not rerun Classification, standalone Tenancy, Jobs, Data transform, or earlier family suites without an affected
  dependency outside the R11-07 complete certification command set.
- Do not stage `tmp/`, inspect private dogfood applications, or use private identities in public docs.
- Do not push, tag, publish, release, deploy, or mutate remote configuration without separate authorization.

## Accepted design laws

- Design from the application inward: business sentence, smallest honest C# expression, exact guarantee, corrective
  failure, then internal types.
- Assess context-aware Web behavior first as ordered contributors. Validate evidence once at the edge and project the
  resulting context through the centralized enforcement seam.
- Persistent Entity semantics belong in Data axes when decoration truthfully selects durable model behavior; they are
  not request-context contributors.
- `AddKoan()` / `Entity<T>` / `EntityController<T>` is the golden business-to-code comparison. Extra public concepts
  must express a real business decision, guarantee, or deliberate override.
- Complexity is centralized at typed responsibility chokepoints. Core owns generic law; pillars own meaning and policy;
  adapters own mechanics; applications own business intent.
- Standard .NET concepts are preferred over Koan ceremony. Break-and-rebuild is justified only by current code and
  consumer evidence.
