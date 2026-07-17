# E11 — The End-to-End Demo (the Epic's Existence Proof)

**Repo(s)**: all three + two machines (or two VMs/containers if the operator says so) ·
**Phase**: D · **Prereqs**: E05, E08, E09, E10 (each link of the chain) · **One to two
sessions** · Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Build and script the single integration artifact the whole analysis converges on — the
first time the stack's headline story runs anywhere, repeatably: **pond ceremony → stones
enroll (CSR) → moss serves mutual TLS → the MongoDB orchestrator emits a replica-set
connection string → a Koan app consumes it over a pond-trusted channel, issuing tokens
bound to its machine identity.** Until this runs, the truthful claim is "shared discovery
substrate"; after it runs — scripted, with tripwires extracted — the claim "integrated
sovereign stack" has an existence proof. This is the ONLY new cross-repo artifact planned
for 2026 (`../05` §1 item 2).

## Context

Every link previously had a verified break; E05/E08/E09/E10 fixed them. Verify each is
actually merged before starting (CHARTER "When blocked"). The consumable seam:
`GET /api/cluster/connect` on the mongodb orchestrator
(`ZEN/src/orchestrators/mongodb/api/cluster.rs:150-169`) returns
`mongodb://…?replicaSet=…`; Koan's satellite (E06) resolves garden intents; Koan's trust
satellite (E10) pins the pond CA and stamps `koi_id` claims. A pre-built Koan sample app
exists to adapt (prefer an existing dogfood — check `KOAN/samples/` for the garden-coupled
sample the satellite work moved, e.g. GardenCoop) rather than writing a new app.

## DECIDED

1. **Topology**: two nodes minimum (one may host pond CA + moss + orchestrator; the second
   a moss stone joining the replica set), plus the Koan app on either. Real machines
   preferred; two VMs or two containers with distinct network identities are acceptable
   with operator approval — record which.
2. **The script is the product**: `EPIC/demo/` (this folder) gets `run.md` (numbered,
   copy-paste honest steps) plus automation where stable (shell/PowerShell per node). Every
   step states what to observe (the boot report line, the roster entry, the connection
   string, the token claims).
3. **Chain assertions** (each must be demonstrated, not assumed): (a) enrollment is CSR —
   capture that no key material appears in transit (E08's test pattern); (b) moss rejects a
   certless mutation (E09); (c) the Koan app's outbound Mongo/API channel validates against
   the pond CA — show a failure when pointed at an untrusted cert; (d) a token minted by
   the app carries `koi_id`/`koi_ca` and validates (E10); (e) pull-the-plug: stop one
   replica member; the app keeps serving (ZG's autonomy claim, user-visible).
4. **Tripwires extracted**: whatever the demo exercised that has no guard gets one —
   minimally, the E07 fixtures gain any shape corrections discovered live, and each repo's
   SURFACES.md rows for the touched surfaces get guard entries + today's date.
5. **Honesty**: the demo doc states the v1 limits plainly (CA-trust-only token binding, no
   PoP; whatever else surfaced).

## DEFAULT

- Database: MongoDB only (STACK-0001 item 9). App: smallest entity-first sample with one
  REST + one MCP-exposed entity.
- A recording (asciinema/screen capture) if cheap — the wedge-demo narrative consumes it
  later.

## Plan of record

1. Preflight: verify E05/E08/E09/E10 merged + versions published; assemble nodes. 2. Walk
the chain manually once, fixing only *demo-path* breakage (file anything else as precise
issues — scope discipline). 3. Script it; run from scratch twice (the second run from a
clean data dir is the repeatability gate). 4. Extract tripwires/fixture corrections.
5. Write `demo/run.md` + limits section. 6. Update SURFACES.md in all three repos. 7. Final
summary: what ran, what was observed at each assertion, what was filed.

## Verification

The second clean-state run, executed from the written steps verbatim (ideally by the dumbest
path: copy-paste only). Each DECIDED-3 assertion observed and captured (output snippets in
run.md).

## Definition of done

- [ ] The chain runs from documented steps on clean state, twice.
- [ ] All five assertions demonstrated with captured evidence.
- [ ] Tripwires + fixture corrections committed; SURFACES.md ×3 updated.
- [ ] `demo/run.md` exists with honest limits; issues filed for off-path findings.
