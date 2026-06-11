# E01 — The Stack Canon ADR (STACK-0001)

**Repo(s)**: all three · **Phase**: A · **Prereqs**: none · **One session**
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Write and commit **STACK-0001 — "The Sylin stack: layering, contracts, and trust topology"**
into all three repos, so that no future session in any repo can contradict the cross-repo
decisions without visibly violating an ADR. Today no document in any repo adjudicates the
stack; that absence caused real divergences (Zen Garden planned to archive the AI crate
Koan's adapter targets; Koan's sovereign profile names orchestrators that are stubs).

## Context

The decisions below were made by the architect in the Epic analysis (`../03` §0, `../04`
R1–R10). Your job is **transcription into ADR form, not design**. Each repo has its own ADR
convention — locate it first: `KOAN/docs/decisions/` (e.g. ARCH-00xx files),
`KOI/docs/adr/` (e.g. 008-embedded-facade.md), and Zen Garden's decision records (search
`ZEN/docs/` for existing ADR/decision files, e.g. ARCH-0030, ORCH-0039, and follow that
convention). Write one canonical document and place a copy (or thin pointer file, per
DEFAULT) in each repo's decision directory.

## DECIDED (transcribe; do not redesign)

1. **Layering law**: Koi → Zen Garden → Koan, strictly acyclic. Knowledge flows up; names
   never flow down. Koi may not name/special-case consumers; ZG depends only on Koi
   (published crates) and never on Koan; Koan consumes siblings only via network contracts
   and satellite packages, never mainline compile-time references.
2. **Contract types per seam**: ZG→Koi = published semver crates; Koan→ZG and Koan→Koi =
   versioned HTTP/SSE contracts published as OpenAPI artifacts; cross-language semantics =
   convention docs + conformance fixtures (the URI-corpus pattern,
   `ZEN/src/common/tests/uri_corpus.rs`).
3. **Coupling form**: "works alone, lights up together" — autonomous fallback is mandatory
   (reference: `KOAN/src/Connectors/Data/Mongo/MongoOptionsConfigurator.cs:74-93`).
4. **Discovery doctrine**: Koi is the sole LAN mDNS/DNS naming authority. The UDP-7184
   garden mesh is ZG-internal, never a cross-project contract (koi-udp bridges containers
   *into* it; it does not export it). Koan keeps only its discovery-candidate pipeline;
   siblings plug in as satellite candidate sources. Koan.ServiceMesh and Koan's raw
   multicast probe are slated for deletion (Koan-side prompt E06/R7).
5. **MCP layering**: Koi = network-substrate MCP (discover/resolve, DNS, certs, health,
   discovery of other MCP servers); Koan = application MCP (entity tools + governance). Koi
   advertises Koan endpoints; it never wraps them.
6. **Trust topology**: two fabrics, one binding. Koi certmesh = machine/channel identity
   (X.509, LAN CA, 30-day certs, roster). Koan Security.Trust/KSVID = workload/agent
   identity (tokens, grants/audit, coherence-epoch revocation). Never merged, never
   independent: a KSVID carries a claim naming the certmesh identity; epoch revocation
   compensates roster-only cert revocation; certmesh+truststore is Koan's cryptographic
   root. Prereqs before claiming mTLS-grade workload identity: CSR enrollment (E08) and
   moss client-auth (E09).
7. **The Koi TLS proxy is outside all stack contracts** until data-plane tests exist and
   `status()` reports truth. The contract surface is: mdns (incl. HTTP/SSE bridge), dns,
   certmesh REST, udp bridging, truststore.
8. **AI succession (joint)**: the `ollama` orchestrator (`ZEN/src/orchestrators/ollama`) is
   the present and the contract target; the `ai` crate's designs are harvested, the crate
   archived with a succession note; Koan's single-endpoint AI adapter targets the ollama
   orchestrator's surface; Koan's Training/Eval facades move to satellite or are cut.
9. **Sovereign composition v1 = Mongo + Ollama.** Postgres/Weaviate are not named in any
   sovereign profile until real choreography exists; the ZG stub orchestrators
   (postgresql/valkey/weaviate) are deleted per ZG's shed register.
10. **Mission canon**: capacitation + enabler doctrine (CHARTER "The mission") binds all
    three projects; nothing in the sovereign path may require an account, external service,
    or telemetry; every capability needs an exit.

## DEFAULT (deviate with one-paragraph justification)

- One canonical full text in `EPIC` + per-repo copies, vs per-repo full copies: default is
  **full copy in each repo** (repos must stand alone), with a header naming the other two
  locations and "edits propagate to all three."
- ADR id: `STACK-0001` in all three repos, filed per each repo's naming style.

## Plan of record

1. Locate each repo's decision directory and one example ADR; mirror its format/front
   matter. 2. Draft STACK-0001 with the ten decisions, each with a one-line rationale and a
   "violated today by / fixed by" pointer (use `../01` §2 ledger evidence). 3. Place in all
   three repos. 4. Cross-link: add a one-line pointer from `KOAN/CLAUDE.md` (Framework
   Utilities/decisions area), `ZEN/.agentic/` context, and `KOI/docs/adr/README` (or
   equivalent index) so per-repo agents discover it. 5. Commit per repo.

## Verification

- Each repo's decision index/listing shows STACK-0001; format matches neighbors.
- Grep each copy for the ten decisions — none missing, none editorialized into new design.

## Definition of done

- [ ] STACK-0001 committed in all three repos, discoverable from each repo's agent context.
- [ ] No new design invented; every decision traces to `../03`/`../04`.
- [ ] Session summary lists the three commit hashes.
