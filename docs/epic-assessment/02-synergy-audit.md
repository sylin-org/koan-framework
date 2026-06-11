# 02 — Synergy Audit: What Works, What Doesn't, and Why It Repeats

## 1. What works (verified, worth protecting)

1. **The discovery column is real end-to-end** — the one fully-functioning vertical thread.
   Moss announces and browses through embedded Koi (Z2/Z3); containers get mDNS without host
   networking (Koi's founding use case, exercised by ZG's containerized orchestrators via
   koi-udp); Koan's `KoiHandler` consumes the mDNS-over-HTTP bridge and maintains a live
   topology projection (KN1). Three languages, one discovery substrate, working today.

2. **Pond-on-certmesh is the right delegation.** Zen Garden runs no CA of its own; the crypto
   is not hand-rolled; rake configures genuine client-side mTLS from enrollment certs and
   installs the CA into the OS truststore (Z4/Z6). ZG's own assessment lists this delegation
   in its do-not-break list. The *design* of the trust seam is correct — the implementation is
   incomplete (§2.1).

3. **The data seam is the most product-shaped artifact in the Epic.** ZG's `ReplicaManager`
   (single-authority check/reconcile) emits ready-to-use `mongodb://…?replicaSet=` connection
   strings over `GET /api/cluster/connect`; Koan's Mongo connector parses the intent and
   auto-resolves — **with autonomous fallback** when no garden is present (N2/N5). "Works
   alone, lights up together" already exists in-tree, once. It is the template every other
   seam should copy.

4. **The cross-language contract corpus exists as a pattern.** The shared Rust/C# URI test
   corpus ("both implementations MUST pass every case", N6) is exactly the discipline the
   Rust/.NET split demands — invented in-tree, applied to one seam, waiting to be generalized.

5. **The layering instinct held.** Koi depends on no sibling; ZG never depends on Koan; Koan
   consumes downward only. Nobody has to untangle a cycle — the Epic's remediation is about
   edge *form*, not edge *direction*. (Rare, and worth saying out loud.)

6. **One coherent design language spans the stack.** The Stone/Moss/Rake/Pond/Garden
   vocabulary demonstrably drove design in two repos and reads as one product family; Koan's
   entity-first grammar plays the same role on the .NET side. The liability is *where* the
   vocabulary lives (inside Koi's substrate — K2), not the vocabulary itself.

7. **The maturation programs already exist and agree.** All three assessments independently
   converge on the same prescription — truth restoration → enforcement → subtraction → launch
   — and each repo already has an operationalized prompt stash. The Epic needs almost no new
   per-repo work invented; it needs cross-repo *decisions* and a handful of seam artifacts.

## 2. What doesn't work (verified)

### 2.1 The trust column is fiction at all three layers simultaneously

The stack's distinguishing word — *sovereign, trusted* — is its least-built property:

| Layer | Verified gap |
|---|---|
| Koi | TLS proxy **regressed silently at the axum 0.8 upgrade** (worked before, per maintainer — see README Corrections; silent panic in `tokio::spawn`; `status()` still hardcodes `running: true`; zero data-plane tests to catch it); revocation is **roster-only** — a revoked cert stays valid to every TLS verifier for up to 30 days (no CRL/OCSP); **no CSR exists** — the CA generates private keys server-side and ships them over the wire (`JoinResponse.service_key`, `RenewRequest.key_pem`); every documented mutation 401s (per-boot `x-koi-token`); loopback-only bind |
| Zen Garden | Moss serves **without client auth** (`with_no_client_auth()`, "mTLS deferred to Phase 4") — the pond's mutual-trust story is one-directional; with pond inactive, :7185 serves **unauthenticated reboot/shutdown/offering-delete**, and `POST /api/v1/stone/deploy` (root code-push) is in *both* route sets — unauthenticated **even with pond active**; `changeme` default passphrase |
| Koan | **Zero X509/certificate-validation code in src** — no CA pinning, no truststore affordance, no TLS posture at all; `KoiHandler` talks plain HTTP; the planned fleet-identity fabric (KSVID, coherence-epoch revocation) is an unfair-asset bullet, not code |

The end-to-end mutually-authenticated path the Epic narrates — *pond ceremony → mTLS fleet →
app consuming a service over a Koi-trusted channel* — has **a verified break at every single
link today**, and the full chain structurally cannot have run with mutual auth (moss has
never verified client certs). Whatever parts of it were exercised in private downstream
solutions (README Corrections), nothing in these repos guards them now.

### 2.2 Every seam is private

Path deps to a sibling checkout (Z1); in-tree contract assemblies referenced by mainline
connectors (N1); hardcoded endpoint strings (KN1); contracts versioned nowhere; conformance
fixtures for exactly one seam (N6). Consequence, compounded by the serialized attention stream
(01 §4): **integration truth has only ever been established on the author's machine, with
sibling checkouts at uncommitted local states** (Koi's local tree is ~6 weeks ahead of its own
public main). The Epic, as experienced by anyone else, has never been tested by anyone — human
or agent.

### 2.3 All three front doors are false at once

Koan: 25/59 front-door claims false, install path cannot succeed (`Koan.*` vs published
`Sylin.Koan.*`, NuGet stale 0.8.x), CI disabled. Zen Garden: README quickstart invokes
artifacts that exist nowhere, zero releases ever so both install scripts fail, 25/130 help
examples fail their own parser. Koi: every documented write example 401s, the headline
container story is broken on native Linux, `cargo install koi-net` is a four-month-stale trap.
A stack adopter must pass three broken front doors **in sequence**; the funnel multiplies
near-zero probabilities. Any public Epic story before this is fixed converts curiosity into
documented "it doesn't work" posts from exactly the audiences being courted.

### 2.4 The two halves of the stack made opposite strategic decisions

- **AI succession, decided opposite ways across the repo boundary.** ZG's strategy leans
  "ollama-now, harvest-ai-designs" — archive the 41.9k-line `ai` crate. But the `ai` crate is
  precisely the component proposed as the *single endpoint* for Koan's `Koan.AI.ZenGarden`
  adapter (the in-repo promotion proposal names it), and Koan's Training/Eval facades are
  implementable **only** by that orchestrator. As written, ZG plans to archive the thing
  Koan's AI-lifecycle story plugs into.
- **The sovereign profile names components that don't exist.** Koan's flagship sovereign
  composition is "Ollama + Postgres + Weaviate on one box" — but ZG's postgresql and weaviate
  orchestrators are ~450-line stubs whose `main()` logs a placeholder and exits, marked
  **Delete** in the shed register, and yet publishable to Docker Hub "as if real" by the build
  scripts. The flagship stack as currently written cannot be choreographed.
- **MCP is claimed twice with no layering doctrine.** Koi's #1-ranked strategic opportunity is
  an MCP server; Koan ships an MCP pillar and plans governed agent access. Same niche, same
  author, no document saying which layer answers what.

### 2.5 Coupling currently *damages* standalone adoption — anti-synergy

The couplings that make the Epic feel real are each repo's own top defect: ZG's #1
critical-path blocker IS the Koi path-dep coupling (blocks clean clone → blocks CI → blocks
releases → blocks contributors); Koan's S3 connector is held hostage by a satellite product
(N3); Koi deferred its own crates.io publishing as "over-abstraction for one consumer" —
blocking koi-embedded/koi-udp from ever serving anyone else. Every deepening of the trilogy as
currently shaped raises all three standalone adoption costs while the integrated path still
doesn't run end-to-end. The N2 fallback pattern is the existing proof that it can be done
right.

## 3. The shared failure-mode analysis (the process finding)

The same four pathologies appear in all three repos, with the same shape:

| Pathology | Koan | Zen Garden | Koi |
|---|---|---|---|
| **Front-door fiction** | 25/59 claims false; ghost Flow pillar | L3 README over L0 distribution; fictional quickstart | All write examples 401; broken container story |
| **Exquisite tests where easy, zero where risky** | Top-decile test platform, *voluntary*: CI disabled, 39/87 test projects outside the sln gate | 2,483 tests that have **never run automatically**; best test density on the *dormant* ai crate | certmesh 264 tests vs **0** proxy data-plane tests; best integration suite guards 686-LOC koi-udp |
| **Generational strata carried in indecision** | 18 duplicate-concept clusters; 90:7 registrar:module | Two full AI orchestrators (57k lines); three backup generations; stub orchestrators | Three orchestrators (main/windows/embedded); vestigial trust profiles; dead FIDO2 |
| **Unwatched broken automation** | Releases to nuget.org gated on build only | Stubs publishable to Docker Hub "as if real" | crates.io publish silently failing since Feb; weekly QA red 10+ weeks |

When the same four defects appear in three codebases in two languages, the cause is not the
codebase — it is the **production process**. All three are AI-amplified solo builds (ZG
discloses 46% AI co-authorship; the `worktree-agent-*` branches on two of the remotes are
physical residue). The structural dynamic: *agents generate at superhuman rate; verification
remains human-rate; and verification is preferentially spent where it is pleasant (in-process
unit tests) rather than where it is risky (data planes, release engineering, strangers'
machines, cross-repo seams).*

The maintainer's correction (README) refines this without changing its force: the operating
model is deliberate — **a single serial lane that matures surfaces by exercising them inside
downstream solutions** (some private, outside these repos). While the lane is on a surface,
verification is excellent — *live*. The failure mode is what happens when the lane rotates
away: the exercising solution stops exercising, no mechanical guard stays behind, and the
surface rots silently (the Koi TLS plane is the type specimen — it worked, an upgrade broke
it, and `status()` kept saying `running: true`). So the prescription is not "verify more" —
the lane has no more hours — it is **"leave a guard at the door when you leave the room"**:
tripwire tests, truth-telling status, and CI that keeps running through dormancy. Formalized
as the surface ledger + rotation contract in [06 §5](06-project-realignment.md).

**The implication for the Epic is sharp.** A cross-repo, cross-language seam is the hardest
possible thing to verify under this process — no shared CI, version skew between path deps and
published artifacts, two toolchains — which means the process *concentrates its signature
failure mode precisely where the stack story lives*. Left alone, the Epic will always be the
least-verified claim in the portfolio. The countermeasures are mechanical, and one of them is
already invented in-tree:

1. **Contract corpora at every seam** (the URI-corpus pattern, N6, extended to offering
   resolution, `/api/cluster/connect`, the mDNS bridge, certmesh join/renew), run in each
   repo's CI against **pinned released artifacts** — never sibling checkouts.
2. **Executable front doors** — README/quickstart commands run verbatim in CI on a clean
   runner. This single gate would have caught the false claims in all three repos
   simultaneously.
3. **Dormancy-safe by construction** — tags, minimal CI, published deps — so the observed
   2-of-3-dormant steady state stops being able to silently break integration.

These three items are the Epic's actual foundation work. Everything in
[03-strategic-opportunities.md](03-strategic-opportunities.md) stands on them.
