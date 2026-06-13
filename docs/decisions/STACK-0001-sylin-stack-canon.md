# STACK-0001: The Sylin stack — layering, contracts, and trust topology

**Status**: Accepted (2026-06-13)
**Date**: 2026-06-13
**Deciders**: Enterprise Architect (Epic analysis `epic-assessment/03`–`04`)
**Scope**: Cross-repo canon for the three Sylin sibling repositories — **Koi** (Rust LAN substrate), **Zen Garden** (Rust fleet orchestrator), **Koan** (.NET application framework). This is the stack's first governed artifact: before it, no document in any repo adjudicated the cross-repo decisions, and that absence caused real divergences (Zen Garden planned to archive the AI crate Koan's adapter targets; Koan's sovereign profile named orchestrators that are stubs).
**Related**: transcribed from `epic-assessment/04` (R1–R10) and `epic-assessment/03` §0; "violated today by" evidence from the interlock ledger `epic-assessment/01` §2. Enforcement prompts: E03–E16.

> **Cross-repo canonical ADR.** Identical copies live in all three repos and must stay in sync — **edits propagate to all three**:
> - `koan-framework/docs/decisions/STACK-0001-sylin-stack-canon.md`
> - `zen-garden/docs/decisions/STACK-0001-sylin-stack-canon.md`
> - `koi/docs/adr/STACK-0001-sylin-stack-canon.md`
>
> This is **transcription, not design**: every decision traces to the Epic analysis cited above. Do not edit a decision here without an upstream architect decision.

---

## Context

The three repositories form a strict layered stack — Koi is depended on by Zen Garden, which is depended on by Koan — but the dependency gradient lived only in code wiring and the architect's head, never in a governed artifact (`01` §2 closing: ZG's docs mention Koan zero times; Koan's mention Koi once; Koi names ZG only as scope-creep provenance). Every cross-repo edge had the wrong *form*: build-time where it should be runtime, private where it should be versioned, with consumer names flowing downhill into the substrate's cryptographic constants (`01` §3). This ADR ratifies the ten decisions that fix the form of every edge and binds all three repos to them.

---

## Decision

The Sylin stack is bound by ten decisions, transcribed from the Epic analysis (`epic-assessment/03` §0 conflicts #1–#5, `epic-assessment/04` R1–R10) with ledger evidence from `epic-assessment/01` §2. This ADR adjudicates the stack; it does not redesign it.

### D1 — Layering law: Koi → Zen Garden → Koan, strictly acyclic; names never flow down

**Decided:** Koi depends on nothing in the family and may not name, special-case, or document its consumers (in code, defaults, or doc-comments). Zen Garden depends only on Koi — published crates at build time, Koi's API at runtime — and never on Koan. Koan consumes both siblings only through network contracts and satellite adapter packages, never mainline compile-time references; sibling names may appear only in satellite packages, and no mainline pillar/connector may reference a satellite. **Knowledge flows up (a consumer may know its provider); names never flow down (a provider that knows its consumer has leaked product into substrate).**
**Rationale:** the gradient already holds directionally (`01` §3); ratifying it as canon with mechanical gates is what stops the leaks the ledger records — under a solo, AI-amplified process, a rule without a gate is a wish.
**Violated today by / fixed by:** Koi names consumers — `koi-certmesh/src/roster.rs:62-63` ("Non-Moss client (e.g. Rake)"), `pond_ceremony.rs`, TOTP issuer `"ZenGarden"`, the `.zengarden` koi-dns zone, `koi-embedded/src/lib.rs:198,215` builder docs (K2); five Koan mainline csproj refs to `Koan.ZenGarden.*` (N1); ZG `Cargo.toml:38` `authors = ["Koan Framework"]` (N7) → fixed by Koi de-consumer-ization (E05/R5), Koan satellite inversion (E06/R4), and gates (Koi greps moss/rake/zengarden/koan outside the frozen allowlist; ZG clean-clone build; Koan arch test asserting no mainline csproj references `Koan.ZenGarden*`).
**Frozen allowlist:** the HKDF domain-separation byte strings `b"pond-unlock-slot-totp-v1"` and `b"pond-fido2-storage-key-v1"` (`koi-crypto/src/unlock_slots.rs:41,551`) are immutable v1 constants — renaming breaks every existing vault — and are allowlisted from the grep gate, never "cleaned up" (K3).

### D2 — Contract types per seam: crates where the language matches, versioned protocols where it doesn't

**Decided:** ZG → Koi (Rust↔Rust) = **published semver crates** (koi-embedded/-certmesh/-common/-crypto/-truststore **+ koi-udp** on crates.io). Koan → ZG and Koan → Koi (.NET↔Rust) = **versioned HTTP/SSE contracts published as OpenAPI artifacts** (the `GET /api/cluster/connect` connection-string shape and the `/v1/mdns/*` bridge are the right form; endpoints documented stable, not hardcoded). Cross-language semantics = **convention docs + published conformance fixtures** (the URI-corpus pattern, `zen-garden/src/common/tests/uri_corpus.rs`), owned by the layer that owns the semantics and consumed downward as test fixtures — never a shared assembly.
**Rationale:** semver carries Rust↔Rust compatibility; versioned protocols carry the language boundary; the only conformance-tested contract in the stack today (the URI corpus, N6) is the template to generalize.
**Violated today by / fixed by:** build-time path deps (Z1), in-tree ProjectReferences (N1), hardcoded endpoints (KN1), and a 119,907-byte hand-rolled `ZenGardenClient` → fixed by crate publication (E03/E04), the contract corpus (E07), and a generated thin client once the OpenAPI specs exist.

### D3 — Coupling form: "works alone, lights up together"

**Decided:** autonomous fallback is mandatory and is the **only** permitted coupling shape. The reference implementation is `koan-framework/src/Connectors/Data/Mongo/MongoOptionsConfigurator.cs:74-93` (resolve a sibling offering if present; fall back to an autonomous connection otherwise). Never add a hard sibling dependency to anything mainline.
**Rationale:** the N2 fallback is the one healthy coupling shape in the stack; ratifying it as canon prevents a mainline data plane from being held hostage to a sibling product.
**Violated today by / fixed by:** the S3 provider's presign **throws** without a Moss endpoint (N3); the Training/Eval facades can **only throw** (N4) → fixed by the S3 split + Training/Eval relocation under Koan's satellite inversion (E06/R4).

### D4 — Discovery doctrine: one seam per layer; the garden mesh stays ZG-internal forever

**Decided:** Koi is the **sole LAN mDNS/DNS naming authority**. The UDP-7184 garden mesh (`stone_chirp`/`tools_beacon`) is **ZG-internal gossip, never a cross-project contract** — koi-udp bridges containers *into* it; it does not export it. Koan keeps only its discovery-candidate pipeline (env → config → container-DNS → localhost → Aspire); siblings plug in as satellite candidate sources. `Koan.ServiceMesh`'s UDP multicast discovery and `ZenGardenClient`'s raw 239.255.42.99 multicast probe are slated for deletion (Koan prompt E06/R7).
**Rationale:** ~6 discovery mechanisms coexist across the stack (KN3/Z7); collapse to one per layer.
**Violated today by / fixed by:** Koan carries three discovery stacks plus `ZenGardenClient`'s raw multicast probe (KN3) → fixed by the E06/R7 deletions, gated by first confirming Koi's `/v1/mdns/subscribe` SSE latency covers any Koan consumer that needed sub-second fleet-presence events (no-stopgaps).

### D5 — MCP layering: substrate MCP vs application MCP

**Decided:** Koi = **network-substrate MCP** (discover/resolve, DNS, certs, health — and discovery *of other* MCP servers via `_mcp._tcp`). Koan = **application MCP** (entity tools + governance). Koi advertises Koan endpoints; **it never wraps them.**
**Rationale:** Koi's #1 opportunity and Koan's MCP pillar claim the same agent interface; the resolution is layering, not arbitration (`03` §0.1).
**Violated today by / fixed by:** no document adjudicates the overlap, so both layers claim the agent surface → fixed by this ADR plus koi-mcp + the `_mcp._tcp` announce satellite + the composed demo (E13).

### D6 — Trust topology: two fabrics, one binding

**Decided:** two layered trust fabrics, never merged and never independent. **Koi certmesh = machine/channel identity** (X.509, LAN CA, 30-day certs, roster). **Koan Security.Trust/KSVID = workload/agent identity** (token envelope, grants/audit, coherence-epoch revocation). A KSVID carries a claim naming the certmesh identity (CN/SAN format pinned in the binding doc) so a token is honored only on a channel whose peer matches; epoch revocation compensates roster-only cert revocation (epoch bump kills a compromised workload in seconds while its cert ages out in ≤30 days); certmesh + koi-truststore is the cryptographic root Koan otherwise lacks (zero-code on .NET via the OS store). **Prerequisites before anyone claims mTLS-grade workload identity:** certmesh CSR-based enrollment (E08) and moss Phase-4 client-auth (E09); until then ship the token fabric with CA-trust-only binding and state the interim honestly. The sovereign profile must work with certmesh alone (no online-issuer assumption).
**Rationale:** the two fabrics patch each other's verified holes; merging them takes the refused SPIFFE/enterprise-PKI road, and full independence forfeits the mutual hole-patching.
**Violated today by / fixed by:** zero certificate-handling code exists in Koan src (KN2, aspirational); moss is `with_no_client_auth()` (Z5); keys travel because there is no CSR enrollment → fixed by E08 (CSR), E09 (moss client-auth), E10 (KSVID binding; tokens carry `koi_id`/`koi_ca`).

### D7 — Koi contract scope: the five proven planes; the TLS proxy is excluded until tested

**Decided:** the formal Koi contract surface is exactly **mdns (register/browse + HTTP/SSE bridge), dns, certmesh REST, udp bridging, truststore**. **The TLS proxy is outside all stack contracts** until data-plane tests exist and `status()` reports truth. Do not build on it; do not delete it either — it is excluded-until-tested, not abandoned.
**Rationale:** a contract is only as good as the substrate's *guarded* surface; the proxy regressed silently after the axum 0.8 upgrade (startup panic; `status()` hardcodes `running: true`) with zero data-plane tests, and neither sibling needs it (moss terminates its own TLS; the builder sets `proxy(false)`).
**Violated today by / fixed by:** the proxy panics and misreports `running: true` (Koi README Corrections) → fixed by excluding it from the contract until data-plane tests + a truthful `status()` re-admit it (R6).

### D8 — AI succession (joint): the ollama orchestrator is the contract target; the `ai` crate is archived

**Decided:** the `ollama` orchestrator (`zen-garden/src/orchestrators/ollama`) is the present and the contract target; the `ai` crate's designs are harvested and the crate is archived with a succession note. Koan's single-endpoint AI adapter targets the ollama orchestrator's surface; Koan's Training/Eval facades move to a satellite package or are cut. This is decided **jointly** — ZG cannot archive the `ai` crate while it is the designated endpoint of `Koan.AI.ZenGarden` and the sole implementor of Koan's Training/Eval surface.
**Rationale:** pick the deployed generation (ollama) now and define the single-endpoint contract on it, rather than freezing against a crate slated for archival.
**Violated today by / fixed by:** `ZenGardenAiAdapter` routes all nine AI capabilities through ZG's orchestrator at priority 0, and Koan's Training/Eval facades can only throw because their sole providers live in the external crate (N4) → fixed by this joint succession ADR plus the Training/Eval relocation/cut (E06; Koan's own MLOps shed).

### D9 — Sovereign composition v1 = Mongo + Ollama

**Decided:** the v1 sovereign composition anchors on the two real orchestrators — **Mongo + Ollama**. Postgres/Weaviate are **not named in any sovereign profile** until real choreography exists. The ZG stub orchestrators (postgresql/valkey/weaviate) are deleted per ZG's shed register.
**Rationale:** name only what is real; either build real Postgres/Weaviate choreography later or stop naming them.
**Violated today by / fixed by:** Koan's sovereign profile names orchestrators that are stubs, and ZG carries stub orchestrators marked Delete → fixed by the sovereign profile + zero-egress CI lane (E14) and the ZG stub deletion (ZG shed register).

### D10 — Mission canon: capacitation + the enabler doctrine binds all three

**Decided:** the mission — **capacitation** (enabling individuals and small teams to take on capabilities usually denied them) under the **enabler-not-competitor** doctrine (export in the incumbent's formats, never require import in ours; be the substrate, not the surface; every capability needs an exit; degrade gracefully when a layer is owned) — binds all three projects. **Nothing in the sovereign path may require an account, an external service, or telemetry; every capability needs an exit.** Honesty is the product: the audience being capacitated is the one most harmed by fictional docs and silent failures.
**Rationale:** the social mission governs every design choice; when a choice trades our surface against feeding a tool the user already runs, the feeder posture wins.
**Violated today by / fixed by:** nothing forces docs or artifacts to be true across the three repos (`02` §3) — Koan's 25/59 false quickstart claims, ZG's failing help examples, Koi's 401-ing examples + dead proxy → fixed by executable front doors and the verifiability package (E15/E16) and the zero-egress sovereign lane (E14).

---

## Consequences

### Positive
- The stack has a single governed artifact: no future session in any repo can contradict the cross-repo decisions without visibly violating an ADR discoverable from that repo's agent context.
- Each decision names its enforcement vehicle (E03–E16), so canon and the work that realizes it are linked.
- The "violated today by" pointers give the next agent the exact ledger evidence (`01` §2) without re-deriving it.

### Negative
- Three full copies must be kept in sync by hand (the DEFAULT chose repo-standalone copies over a single source + pointers). The header's propagation note is the only guard; a future surface ledger (E02) or a CI string-equality check on the **Decision** block would harden it.

### Neutral
- This ADR is transcription, not design. Decisions change only upstream (a new architect decision in `epic-assessment/03`–`04` or a successor STACK-000x), then propagate to all three copies.
- Filenames are identical across all three repos by intent (cross-repo grep-identity), which slightly bends Koi's 3-digit ADR-numbering convention; see that repo's copy header.

---

## Alternatives considered

- **One canonical full text in `epic-assessment/` + thin per-repo pointer files.** Rejected per the card's DEFAULT: repos must stand alone, so each carries the full copy with a propagation header. (DEFAULT honored, not deviated.)
- **Unify the three self-description schemas / share a cross-language library.** Rejected — repeats ZG's orchestrator-common mistake (1,359 lines of shared abstraction the flagship consumers declined). Cross-language sharing happens only as protocols, JSON schemas, and conformance fixtures (D2; see R9).
- **Merge the two trust fabrics into one PKI.** Rejected — takes the refused SPIFFE/enterprise-PKI road and forfeits the mutual hole-patching that makes epoch revocation and certmesh complementary (D6).

---

## References

- `epic-assessment/04-architecture-alignment.md` (R1–R10) — the seam design transcribed here.
- `epic-assessment/03-strategic-opportunities.md` §0 — the five cross-project conflicts (MCP, discovery, AI succession, sovereign composition, coupling form).
- `epic-assessment/01-stack-anatomy.md` §2 — the verified interlock ledger (Z*/K*/N*/KN* evidence).
- `epic-assessment/prompts/CHARTER.md` — the shared session contract (mission, canon, frozen constants).
