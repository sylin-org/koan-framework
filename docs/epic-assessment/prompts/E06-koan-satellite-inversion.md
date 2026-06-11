# E06 — Koan Satellite Inversion (Works Alone, Lights Up Together)

**Repo(s)**: Koan · **Phase**: B · **Prereqs**: E01 (STACK-0001) · **One to two sessions**
(split point marked below) · Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Remove every mainline compile-time reference to ZenGarden from Koan, inverting the coupling
through neutral extension points into self-registering satellite packages — so Koan works
alone, and referencing a satellite is what lights the garden up (Reference=Intent applied to
the stack itself). Enforce with an architecture test so the references cannot regrow.

## Context (verify each)

- **Five mainline references today** (re-verified 2026-06-11):
  `src/Connectors/Data/Mongo/Koan.Data.Connector.Mongo.csproj:16`,
  `src/Connectors/AI/Ollama/Koan.AI.Connector.Ollama.csproj:18`,
  `src/Connectors/Data/Vector/Weaviate/...Weaviate.csproj:19` → `Koan.ZenGarden.Core`;
  `src/Connectors/Storage/S3/Koan.Storage.Connector.S3.csproj:12` → the **full**
  `Koan.ZenGarden` client; plus `src/Connectors/AI/ZenGarden/` (the dedicated connector —
  stays, but becomes the satellite).
- The hot-path leak: `MongoOptionsConfigurator.cs:74-93` parses `ZenGardenConnectionIntent`
  and auto-resolves via `IZenGardenInitializationProvider` — **with autonomous fallback**;
  this fallback behavior is the pattern to preserve, the ZG-typed seam is what moves.
- S3 severity: presign throws without a Moss endpoint (Koan assessment
  `01-cartography.md:98-99`); the bridge runs through a ~120KB single-file `ZenGardenClient`.
- The neutral pipeline already exists: `src/Koan.Core/Orchestration/
  ServiceDiscoveryAdapterBase.cs:80-146` (env → explicit config → container-DNS → localhost
  → Aspire candidates).
- Koan-side context: read `KOAN/docs/assessment/06-prompt-stash.md` PREAMBLE and follow the
  repo's KoanModule/auto-registrar conventions (`KOAN/CLAUDE.md`).

## DECIDED

1. **Neutral seam in Koan.Core.Orchestration**: hoist offering resolution as
   `IOfferingResolver` (adapter-id → offering mapping + resolve-to-connection-candidates),
   provider-neutral names, no ZG types; `ServiceDiscoveryAdapterBase` gains one pluggable
   candidate-source extension point. The seam's altitude is "produce connection-string
   candidates / map adapter→offering" — if it grows ZG-shaped parameters, it has failed.
2. **Satellite packages** (self-registering via the repo's standard auto-registration):
   `Sylin.Koan.ZenGarden` (client + KoiHandler + intent parsing + the offering bindings) and
   the AI connector as `Sylin.Koan.AI.ZenGarden`. Mainline connectors lose their
   ProjectReferences; behavior with the satellite referenced must be identical to today.
3. **S3 split**: the mainline S3 connector works against any S3 endpoint; Moss-presign moves
   to the satellite (or capability-gates on it). Mainline presign without a provider =
   fail-loud `CapabilityNotSupportedException`-style error, not a Moss dependency.
4. **Training/Eval facades** that can only throw (providers live only in ZG): move to the
   satellite or cut per Koan's own MLOps shed — pick whichever the joint AI-succession
   decision (STACK-0001 item 8) makes cleaner; do not leave mainline facades that throw.
5. **Architecture test** (the R1 gate): a unit test in the main solution asserting no
   mainline csproj references `Koan.ZenGarden*` (parse csproj XML; allowlist = satellites +
   their tests).
6. Samples/dogfoods that exercise the garden path (e.g. g1c1) reference the satellite — the
   end-to-end garden test must not be lost.
7. Greenfield posture: no `[Obsolete]` bridges; the old direct references are deleted, not
   shimmed.

## DEFAULT

- Split point if two sessions: **6a** = neutral seam + Mongo/Ollama/Weaviate inversion +
  arch test; **6b** = S3 split + AI connector/Training-Eval disposition + samples sweep.
- Satellite folder location: keep under `src/` for now (publishing/repo split is a later
  operator decision); NuGet ids use the `Sylin.` prefix like all published packages.

## Plan of record

1. Map all ZenGarden type usages (`grep -r "ZenGarden" src/ --include=*.cs` by project).
2. Design `IOfferingResolver` mirroring the existing interfaces' shape (they are nearly
right already: `IZenGardenOfferingBinding` adapter-id→offering, provider resolution).
3. Implement seam; move ZG implementations to the satellite; rewire the four connectors to
the neutral seam. 4. S3 split per DECIDED 3. 5. Arch test. 6. Run the full build + affected
integration suites (the repo's green-ratchet rules apply — `KOAN/tests/README.md`). 7. Docs:
update connector docs + the satellite README ("works alone, lights up together" stated).
8. SURFACES.md row ("ZenGarden bridge → satellite; guard = arch test + integration suite").

## Verification

- `Koan.sln` builds with zero `Koan.ZenGarden*` references from mainline (arch test green).
- With satellite referenced (sample/dogfood): garden resolution works as before.
- Without it: Mongo/Ollama/Weaviate/S3 connectors function against plain endpoints; S3
  presign fails loud and clear, not with a missing-Moss crash.

## Definition of done

- [ ] Five mainline references gone; arch test enforces it.
- [ ] Satellites self-register; identical lit-up behavior; samples moved.
- [ ] S3 generic + satellite presign; no throwing mainline facades remain.
- [ ] Green ratchet respected; SURFACES.md updated.
