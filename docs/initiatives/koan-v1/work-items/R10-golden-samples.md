---
type: SPEC
domain: framework
title: "R10 - Graduate the Golden Sample Portfolio"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: GardenCoop, exact inventory, S1.Web, S0, S10, g1c2, and public documentation passed; R10-07 S14 workload-lab rebuild active
---

# R10 — Graduate the golden sample portfolio

- Tranche: `T7B — V1 release readiness / maintained-sample graduation`
- Status: `in-progress`
- Depends on: passed R09 and R08-04
- Guards: R08-05 initial coherent public observation
- Owner: maintained sample truth, application ergonomics, executable business proof, and curriculum order

## Mandate

Every active sample is a golden, executable example of current Koan use and good .NET practice.
A sample is not allowed to remain “illustrative,” compile only through warnings, rely on old bootstrap
workarounds, or advertise behavior that is absent from its executable evidence.

`assess` and `incubate` are temporary R10 migration states, not acceptable V1 outcomes. By the portfolio
boundary, every project still presented outside the explicit historical archive must graduate. Material
that is intentionally not modernized must move outside the active curriculum or be deleted; directory
existence never earns sample status.

Samples are dogfood applications: their mental models must survive real hosting, persistence, APIs,
capability composition, inspectability, shutdown, and any deployment shape they claim. A failure is first
classified at its real owner—framework, pillar, adapter, application, documentation, or sample tooling.
Sample-local compatibility glue is not an accepted repair for a framework defect.

## Portfolio outcome

A developer or coding agent can choose any maintained sample and see:

- business intent mapped directly to Entity models, controller declarations, lifecycle/capability semantics,
  and business-named workflows;
- parameterless `AddKoan()` as the normal host expression, with extra code only for genuine application intent;
- Reference = Intent through deliberate standard project/package references;
- truthful startup decisions, guarantees, health, and corrective failures;
- one documented command that reaches a meaningful business result;
- focused executable proof that prevents the sample from quietly rotting.

## Golden-sample laws

1. **Business first.** Application source reads as domain state, rules, workflows, and boundaries—not framework
   assembly mechanics.
2. **One canonical expression.** No manual `AppHost`, registrar/initializer compatibility, duplicate provider
   selection, or sample-only bootstrap path.
3. **Entity and controllers first.** Use Entity statics and `EntityController<T>` before services/repositories;
   custom HTTP remains controller-owned.
4. **References state intent.** Every direct capability reference is deliberate; redundant or accidental
   references are removed. External prerequisites are explicit and testable.
5. **Strict source truth.** The sample and its referenced shipping projects build warning-free under the
   selected strict lane. Framework warnings uncovered by a sample are repaired at the framework owner.
6. **Meaningful executable proof.** Each sample asserts its defining business result plus host/composition facts;
   compilation alone is insufficient.
7. **Claimed deployment shapes execute.** Container, NativeAOT, package-only, or external-provider claims require
   matching evidence or are removed/qualified.
8. **Agent legibility.** One obvious entry point, one obvious application-composition owner when needed, canonical
   APIs, corrective errors, and a compact README concept budget.
9. **Portfolio truth.** “Maintained” means present in `Koan.sln`, exercised by the appropriate CI lane, documented
   in the canonical sample index, and assigned a maturity/status backed by evidence.
10. **No private dogfood identity.** Only public repository samples and anonymous reproductions become evidence.

## Graduation inventory

The first inventory is intentionally evidence-seeking, not a promise. The current public sample index, physical
projects, and solution membership disagree: S3 is described but absent, S6 is listed as broken but is in the
solution, S7 exists outside it, and S19/S20 exist without canonical catalog entries. R10 must reconcile these
surfaces by graduating, incubating, archiving, or deleting each project explicitly.

GardenCoop goes first because it spans the common host, Entity persistence, relationships, Lifecycle, controllers,
Auth/Admin, OpenAPI, runtime facts, startup seed, static UI, and a NativeAOT claim without external infrastructure.
Its completed slice defines the reusable evidence template; it does not mechanically dictate architecture to
samples with different business meaning.

## Per-sample discovery

Before editing a sample:

1. state its business sentence and smallest honest application expression;
2. enumerate direct references, configuration, infrastructure, context, and deployment prerequisites;
3. run a strict baseline build and the documented meaningful path;
4. identify obsolete application ceremony and framework defects separately;
5. choose one composition owner and delete superseded paths;
6. add business, host, facts, error, and claimed-deployment evidence proportionate to the sample;
7. update the canonical sample index only after evidence passes.

## Acceptance

R10 passes only when:

1. every project presented outside the explicit historical archive is graduated or removed from the active portfolio;
2. every active sample satisfies all ten golden-sample laws; `assess` and `incubate` have reached a terminal disposition;
3. `samples/README.md`, physical projects, `Koan.sln`, CI selection, and executable evidence agree;
4. every advertised shortest path and special deployment shape executes from a clean checkout;
5. no sample teaches an obsolete or second canonical Koan mechanism;
6. focused sample proof is economical during migration, followed by one portfolio boundary run before R08-05;
7. remaining non-graduated samples cannot be mistaken for supported curriculum or product evidence.

## Child slices

| ID | Sample | Status | Meaningful proof |
|---|---|---|---|
| [R10-01](r10/R10-01-gardencoop.md) | g1c1.GardenCoop | passed | sensor reading → binding → dry reminder/recovery, HTTP/facts/dashboard, host lifecycle, NativeAOT runtime |
| [R10-02](r10/R10-02-portfolio-inventory.md) | portfolio inventory | passed | exact project/solution/docs inventory, explicit queue dispositions, S1.Web selected next |
| [R10-03](r10/R10-03-s1-web.md) | S1.Web | passed | deterministic task graph → scalar/set/stream relationships, generated HTTP, cache/SQLite facts, dashboard |
| [R10-04](r10/R10-04-s0-console-json.md) | S0.ConsoleJsonRepo | passed | standard console host → local JSON checklist → materialized query → clean process/facts |
| [R10-05](r10/R10-05-s10-devportal.md) | S10.DevPortal | passed | local editorial draft → approved publication → named SQLite/Mongo/Postgres channel → transfer/facts proof |
| [R10-06](r10/R10-06-g1c2-gardencoop-embedded.md) | g1c2.GardenCoopEmbedded | passed | local Produce save → ONNX embedding → sqlite-vec search → HTTP/facts/self-contained-folder proof |
| [R10-07](r10/R10-07-s14-workload-lab.md) | S14.WorkloadLab | in-progress | bounded order intake → named source → verified durable receipt → capabilities/facts/correction proof |
| [R10-08](r10/R10-08-public-documentation.md) | public documentation | passed | greenfield front door/TOC → current product/package companions → enforced truth gate → FirstUse/product-surface proof |

Open later children only after the GardenCoop slice establishes the evidence template and a focused inventory
selects the next highest-value maintained sample.

## Stop conditions

- Stop if a sample repair hides a framework defect or preserves a legacy path for convenience.
- Stop if “golden” is inferred from solution compilation without a business result.
- Stop if one generic test abstraction erases sample-specific meaning.
- Stop if a deployment claim cannot be executed responsibly; qualify or remove it instead.
- Stop before R08-05 publication until the graduated portfolio boundary is green.
