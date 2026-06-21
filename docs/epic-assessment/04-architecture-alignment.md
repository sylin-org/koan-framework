# 04 — Architecture Alignment: The Target Seam Design

Ten recommendations (R1–R10). The first is a law; the rest follow from it. Each names its
verified motivating defect and its enforcement mechanism — under a solo, serialized,
AI-amplified process ([02 §3](02-synergy-audit.md)), **a rule without a mechanical gate is a
wish**.

## R1 — The layering law: Koi → Zen Garden → Koan, strictly acyclic, names never flow down

The natural gradient already holds directionally (01 §3); ratify it as canon in **one
cross-repo ADR referenced from all three repos** (the Epic's first governed artifact — today
no document anywhere adjudicates the stack):

1. **Koi depends on nothing in the family and may not name, special-case, or document its
   consumers** in code, defaults, or doc-comments. *(Violated today: roster.rs "Non-Moss
   client (e.g. Rake)", pond_ceremony.rs, TOTP issuer "ZenGarden", `.zengarden` zone comments,
   "Moss discovery UDP" builder docs — 01 §2 K2.)*
2. **Zen Garden depends only on Koi** — published crates at build time, Koi's API at runtime —
   and never on Koan. Scrub `authors = ["Koan Framework"]` from ZG's Cargo metadata (N7).
3. **Koan consumes both siblings only through network contracts**, never mainline compile-time
   references; sibling names may appear only in satellite adapter packages; no mainline
   pillar/connector may reference a satellite. *(Violated today: five mainline csproj refs —
   N1.)*

**Knowledge flows up (a consumer knows its provider); names never flow down (a provider that
knows its consumer has leaked product into substrate).** Gates: Koi greps for
moss/rake/zengarden/koan outside a frozen-constants allowlist; ZG builds a clean clone with
`[patch.crates-io]` stripped; Koan runs an architecture test asserting no mainline csproj
references `Koan.ZenGarden*`.

## R2 — Contract-type matrix: crates where the language matches, versioned protocols where it doesn't

| Seam | Contract type | Concrete form |
|---|---|---|
| ZG → Koi (Rust↔Rust) | **Published semver crates** | koi-embedded/-certmesh/-common/-crypto/-truststore **+ koi-udp** on crates.io; semver is the compatibility promise |
| Koan → ZG (.NET↔Rust) | **Versioned HTTP contracts** (OpenAPI release artifacts) | (a) the offering/connect API — `GET /api/cluster/connect` returning opaque connection strings is already the right shape; (b) the AI-orchestrator single endpoint — frozen only **after** conflict #3's succession ADR, or you version the loser |
| Koan → Koi (.NET↔Rust) | **Versioned HTTP/SSE contract** + OS-truststore convention | `/v1/mdns/*` bridge (documented stable, not hardcoded strings); koi-truststore installs the CA, .NET trusts the OS store natively (verified: zero code needed); the future koi-mcp surface is the agent-facing form of the same contract |
| Cross-language semantics | **Convention doc + published conformance fixtures** | The URI corpus pattern (N6): owned by the layer that owns the semantics, consumed downward as test fixtures — never a shared assembly |

The 119,907-byte hand-rolled `ZenGardenClient` becomes a thin generated client in a satellite
package once the OpenAPI specs exist.

## R3 — Fix Z1: ZG's five `../koi` path deps → crates.io versions + `[patch.crates-io]`

The cheapest, highest-leverage fix in the Epic — ZG's own critical-path #1, since clean-clone
→ CI → releases → contributors are all hard-blocked behind it. `[patch.crates-io]` pointing at
`../koi` preserves the sibling dev loop. **Two verified blockers on the Koi side**: koi-udp is
not on crates.io (and moss builds koi-embedded with `udp(true)`, so the published koi-embedded
must carry the udp feature — Koi must publish the full dependency closure), and Koi's publish
pipeline has been silently broken since February, so the five published versions are plausibly
**stale** relative to the API surface ZG compiles against today. **Verify compile
compatibility before the swap, not after**; republish first if needed. Enforce permanently
with a CI job that builds with the patch section stripped.

## R4 — Fix N1: invert Koan's mainline ZenGarden references through neutral extension points

Koan already owns the right seams; the inversion is cheap:

- Hoist offering-resolution into `Koan.Core.Orchestration` under **provider-neutral names**
  (`IOfferingResolver`, adapter-id → offering mapping) — it generalizes an existing in-tree
  pattern with four consumers (Mongo/Ollama/Weaviate/S3), satisfying the repo's own ≥2-usages
  dogfood bar.
- The discovery-candidate pipeline (`ServiceDiscoveryAdapterBase`: env → config →
  container-DNS → localhost → Aspire) gains one pluggable candidate source.
- ZG-specific pieces — `ZenGardenConnectionIntent` parsing, the offering bindings,
  `KoiHandler` — move to **`Sylin.Koan.ZenGarden.*` satellite packages** that self-register
  via Reference=Intent: referencing the satellite is what enables garden resolution. Exactly
  the framework's own philosophy.
- **S3 is the severest case and gets split**: a generic S3 connector that works against any
  endpoint; Moss-presign behavior moves to the satellite or behind a capability gate. A
  mainline storage connector whose presign throws without a sibling product (N3) is a
  product-in-the-data-plane defect, full stop.
- The Training/Eval facades that can only throw (N4) move to the satellite or are cut per
  Koan's own MLOps shed.

Risks to manage: the neutral seam must stay at "produce candidates / map offering" altitude
(if it grows ZG-shaped parameters it just relocates the leak); the g1c1 dogfood and samples
must move to the satellite reference or Koan loses its only end-to-end garden test.

## R5 — Fix K1/K2: make Koi's hidden second customer an honest public one

Do **not** delete koi-udp or embedded HTTP self-hosting — they are well-built contracts with a
real consumer, formally declared in KOI-0001. The defect is that the contract is invisible and
the API surface is consumer-shaped. Four moves:

1. **Relabel honestly** (Koi's own assessment verdict): koi-udp is the container-bridging edge
   case and sibling-orchestrator substrate, not a peer pillar; README/GUIDE state that a
   sibling fleet orchestrator is a first-class consumer.
2. **De-consumer-ize the API**: parameterize the TOTP issuer (`"ZenGarden"` → config),
   genericize roster role docs, firewall-prefix examples, `.zengarden` zone comments — ZG
   passes its branding through configuration.
3. **Freeze the cryptographic domain-separation constants at the neutral `b"koi-…-v1"`
   namespace** (`b"koi-unlock-slot-totp-v1"`, `b"koi-promote-v1"`, `b"koi-seal-group-v1"`):
   they are opaque HKDF inputs whose renaming breaks every existing vault and enrolled slot.
   The original `pond-*` strings were renamed once in the pre-1.0 greenfield window (no
   production vault existed); frozen at `koi-*` from here — a new algorithm gets a new
   versioned label, never a rename. *(Post-1.0 this is the no-stopgaps rule pointed the other
   way: cosmetic relabeling would be the band-aid that breaks production.)*
4. **Fix the two contract bugs as contract bugs**, because they break exactly the programmatic
   consumers the Epic depends on: the undocumented per-boot `x-koi-token` (→ documented,
   stable token provisioning for programmatic consumers) and the loopback-only bind with no
   flag. **Sequence: token story first, bind flag after** — opening the bind before the auth
   story widens the attack surface on a daemon whose mutations are gated by a value only the
   daemon process knows.

User-visible string changes (TOTP issuer in authenticator apps, firewall rule names, audit
event names) are breaking for existing enrolled meshes — document a re-enroll step.

## R6 — Scope the Epic's Koi contract to the five proven planes; exclude the TLS proxy

A contract is only as good as the substrate's *guarded* surface, and the proxy is the type
specimen of the unguarded kind: it worked before the axum 0.8 upgrade, regressed silently
(startup panic; `status()` hardcodes `running: true`), and has zero data-plane tests to say
so (README Corrections). Fortunately **neither sibling needs it**: moss terminates its own
TLS from certmesh certs; rake does client-side TLS; moss's builder already sets
`proxy(false)`. The working
pattern is *"certmesh issues, consumers terminate"* — not *"Koi proxies."* The formal contract
surface is therefore exactly what the siblings already consume: **mdns (register/browse +
HTTP/SSE bridge), dns, certmesh REST, udp bridging, truststore**. The proxy is re-admitted
only after data-plane tests exist and `status()` reports truth — excluded-until-tested, not
abandoned (Koi's own "OrbStack domains" opportunity still needs it).

**Extension (ADR-020, operator-ratified 2026-06-20).** The contract surface additionally
includes the **mode-transparent trust primitives' wire contract** — the signed `Envelope`
(versioned, carry-cert, ES256 over canonical bytes; verify returns an assurance level, not
a bool), the `Posture` descriptor (the orthogonal `signed`/`encrypted` booleans →
`open`/`authenticated`/`confidential`, stamped advisorily into mDNS TXT), and the
**same-port dual-mode transport handshake** (one socket serving plaintext and mTLS,
dispatched per connection by a ClientHello sniff so a posture flip never drops an in-flight
connection). These are published **language-neutrally** so a non-Rust sibling can implement
byte-identical primitives — the realization is `koi/docs/reference/trust-protocol.md`
(Posture, Envelope, Sealed, dual-mode handshake, diagnose), with certless conformance
vectors + a validator for cross-language siblings. The reserved confidentiality rung
(`Sealed`, group-key AEAD under a K3-distinct HKDF label) is named-but-not-yet-produced.
This extends R6's five planes; it does not re-admit the proxy.

## R7 — One discovery seam per layer; the garden mesh stays ZG-internal forever

Collapse ~6 mechanisms to one per layer: **Koi owns LAN mDNS/DNS naming. ZG owns fleet
presence** via its own UDP-7184 mesh (`stone_chirp`/`tools_beacon` is a private wire format —
never a cross-project contract; koi-udp bridges containers *into* it, it does not export it).
**Koan owns only the candidate pipeline**, into which siblings plug per R4. Two deletions
follow on the Koan side: `Koan.ServiceMesh`'s UDP multicast discovery (already on Koan's own
shed list) and `ZenGardenClient`'s raw 239.255.42.99 multicast probe — .NET consumers get
mesh state via Koi's bridge or ZG's HTTP APIs. One verification gate before deleting the
probe (per the no-stopgaps rule): confirm Koi's `/v1/mdns/subscribe` SSE latency actually
covers any Koan consumer that needed sub-second fleet-presence events.

## R8 — Shared substrate #1: the "release truth" convention

Three repos, one disease ([02 §3](02-synergy-audit.md)): nothing forces docs or artifacts to
be true. Because the toolchains differ, the substrate is a **convention plus ≤3 reusable CI
workflow templates** (cargo + dotnet flavors), not shared code:

- **(a) Tag-driven releases** binding every artifact (crate, NuGet package, container,
  binary) to a git SHA — NBGV-style discipline generalized. This is also what makes ZG's
  install scripts and Koan's NuGet story functional at all.
- **(b) Executable front doors** — every documented quickstart/example runs verbatim in CI on
  a clean runner. The one gate that would have caught Koan's 25/59 false claims, ZG's failing
  help examples, and Koi's 401-ing examples + dead proxy *simultaneously*.
- **(c) Cross-repo contract jobs** pinned to **released** upstream artifacts: ZG builds
  against published koi crates with the patch stripped (R3's gate); Koan's integration lane
  spins a released ZG container and asserts `/api/cluster/connect`, and a released Koi binary
  and asserts `/v1/mdns/discover` — making R2's contracts regression-tested instead of
  aspirational.

Bootstrap order is strict: R3 first (ZG CI is hard-blocked), Koan's sln-coverage repair
(39/87 test projects invisible) before its green gate means anything. Bootstrap (c) against
pinned SHA-built artifacts until first tags exist. Keep the template repo to ≤3 workflows or
it becomes the next orchestrator-common.

## R9 — Shared substrate #2: one self-description **envelope**, three domain schemas

Koan's boot report + planned lockfile, Koi's roster/status, ZG's manifest/observe are the
same instinct over three genuinely different domains. **Do not unify the schemas** — that
would repeat ZG's orchestrator-common mistake (1,359 lines of shared abstraction that the
flagship consumers declined; its only users are the stub orchestrators marked Delete).
Standardize the **envelope**: one versioned convention defining component id, version+SHA,
capability list, declared contract endpoints+versions, health, provenance timestamp — served
at a well-known path and exposed as an MCP resource — with each project's domain payload
namespaced inside (`koan.composition`, `garden.manifest`, `koi.roster`). Payoffs: the
cross-layer drift audit ([03 §6](03-strategic-opportunities.md)), one uniform agent
introspection surface (Koi's #1 opportunity *and* Koan's agent-native operational half), and
the same document doubles as the artifact R8's truth gates verify. Keep the envelope ≤ a
dozen fields; validate each repo's emitted document against the published JSON Schema in CI
(envelope conventions rot fastest because nothing compiles against them). This is the one
place the Epic standardizes *across* the language boundary — precisely because it is data,
not code.

## R10 — Trust fabric: two layered fabrics with one documented binding

**Certmesh = machine/channel identity** (X.509, LAN CA, 30-day certs, roster). **Koan
Trust/KSVID = workload/agent identity** (token envelope, grants/audit, coherence-epoch
revocation). Never merged — Koi's shed list explicitly refuses the SPIFFE/enterprise-PKI
road, and Koan's epoch revocation rides its own coherence channel, not a PKI distribution
point. Never independent — they patch each other's verified holes: epoch bump kills a
compromised workload's access in seconds while its transport cert ages out in ≤30 days
(bounding blast radius from both ends); certmesh + koi-truststore supplies the cryptographic
root Koan entirely lacks (zero-code on the .NET side via the OS store).

**The binding is a convention doc, not code**: a KSVID carries a claim naming the certmesh
identity (CN/SAN format pinned in the doc) so a token is honored only on a channel whose peer
matches. **Two prerequisites before anyone claims mTLS-grade workload identity**: certmesh
CSR-based enrollment (the single biggest PKI-correctness fix in the stack — keys that
traveled are not proof-of-possession anchors) and moss's Phase-4 client-auth. Until then,
ship the token fabric with CA-trust-only binding and state the interim honestly. The
sovereign profile must work with certmesh alone — keep the binding free of any online-issuer
assumption. If Koan's threat model ever needs shorter machine-cert exposure than the
hardcoded 30 days, that is a Koi change request through the contract, not a Koan workaround.
