# E12 — The Self-Description Envelope (One Convention, Three Payloads)

**Repo(s)**: all three · **Phase**: D · **Prereqs**: E01; deepens when Koan's lockfile card
(KOAN 07-P1.1) lands — do not block on it · **One session** · Read
[CHARTER.md](CHARTER.md) first.

## Mission

Standardize how every layer states what it is: one versioned **envelope** JSON Schema with
per-project domain payloads — Koan's composition (boot report now, lockfile when P1.1
lands), Zen Garden's garden manifest, Koi's roster/status — served at a well-known path and
exposed as an MCP resource where MCP exists. This unlocks the composition audit nothing
else on the market offers (diff *shipped* vs *placed* vs *trusted*) and gives agents one
uniform introspection surface across the stack.

**Do not unify the domain schemas.** That repeats Zen Garden's orchestrator-common mistake
(1,359 lines of shared abstraction its flagship consumers declined). The envelope is ≤ a
dozen fields; domains stay namespaced payloads.

## Context (verify each)

- Koan: boot reports are core canon ("Self-Reporting Infrastructure", `KOAN/CLAUDE.md`);
  the lockfile capability (behavioral SBOM from the source-gen registry) is designed in
  `KOAN/docs/assessment/07-strategic-prompt-stash.md` card P1.2 area — reuse its shapes,
  don't reinvent.
- Koi: `GET /v1/status` exists; certmesh exposes status/roster/log
  (`KOI/crates/koi-certmesh/src/http.rs:40-58`).
- Zen Garden: moss carries garden/fleet state (observe/tending surfaces — locate the
  current state endpoints in `ZEN/src/moss/src/api/` before designing).

## DECIDED

1. **Envelope fields (v1, closed list)**: `id` (component identity), `kind`
   (koan-app | koi-daemon | zen-moss | zen-orchestrator | …), `version`, `sha` (VCS),
   `capabilities` (string list), `contracts` (list of {name, version, endpoint}), `health`
   (ok | degraded | failing + one message), `generatedAt`, `payloadType` (namespace), and
   `payload` (the domain document). Nothing else; domain semantics never creep up.
2. **Schema home**: `EPIC/contracts/self-description/v1/envelope.schema.json` (this folder)
   + a copy vendored into each repo with the pinned version recorded (E07's pattern).
3. **Well-known path**: `GET /.well-known/sylin/self.json` on every HTTP-serving component
   (Koan apps via an endpoint contributor following the repo's WEB conventions; moss; the
   koi daemon). Components without HTTP (CLI) get `<binary> self --json`.
4. **Payloads v1 = what exists today**: Koan emits its boot-report summary (modules,
   providers, capabilities); Koi emits status+roster summary; moss emits stone+offerings
   manifest. When Koan's lockfile lands, it replaces/joins the payload — the envelope does
   not change.
5. **CI validation**: each repo gains a test that boots the component (or calls the
   generator) and validates the emitted document against the vendored schema — envelope
   conventions rot fastest because nothing compiles against them; this is the guard.
6. MCP: where an MCP surface exists (Koan today; Koi after E13), the same document is
   exposed as an MCP resource.

## DEFAULT

- Truthful minimalism beats completeness: a 6-capability honest list over a 40-item
  aspirational one. When in doubt, emit less.
- `sha` from build-time stamping per each repo's existing versioning (Koan: NBGV).

## Plan of record

1. Write the schema + a short `contracts/self-description/README.md` (rationale, the
anti-unification rule). 2. Per repo: implement the endpoint/command emitting envelope +
current-truth payload; 3. add the schema-validation test; 4. vendor + pin the schema.
5. Wire MCP resource exposure where MCP exists. 6. SURFACES.md rows (guard = the validation
test). 7. A 10-line "composition audit" example in the README: fetch all three documents on
a demo setup and diff identities/contract versions.

## Verification

Each component's emitted document validates against the schema in CI-runnable tests; the
three documents from a local run can be joined on `contracts` entries (names match E07's
fixture names — keep them consistent).

## Definition of done

- [ ] Schema v1 committed here + vendored ×3; emitters live in all three repos.
- [ ] Validation tests green; MCP resource where applicable.
- [ ] Contract names consistent with E07; SURFACES.md updated ×3.
