# 01 — Stack Anatomy: What the Epic Actually Is

## 1. The declared thesis

Read together, the three projects describe a four-plane vertical:

| Plane | Owner | Capability |
|---|---|---|
| Application | **Koan** | Entity-first .NET apps: REST + MCP + cache + jobs + embeddings from one model declaration |
| Data / AI choreography | **Zen Garden** (orchestrators) | Self-healing MongoDB replica sets; VRAM-aware Ollama placement across mixed GPUs |
| Fleet / hardware | **Zen Garden** (moss/rake/lantern) | Scavenged laptops, thin clients, Pis, Android phones as a designed-for substrate; offerings lifecycle, self-update with rollback |
| Network / naming / trust | **Koi** | mDNS discovery, local DNS, zero-config private CA (certmesh), OS truststore install |

The composed promise: *an agent builds a Koan app in one session; Zen Garden places it and
choreographs its database on hardware you own; Koi names it, certs it, and makes it
discoverable — no account, no YAML, no cloud.* Koan's "sovereign/scales-down" second-act
capability ("Ollama + Postgres + Weaviate on one box") is literally a Zen Garden stone running
a Koan app with Koi-minted certificates.

## 2. The verified interlock ledger

Every cross-project coupling, re-derived from source for this analysis. **Status legend**:
✅ real (works as wired) · 🟡 half-real (wired, materially incomplete) · ❌ broken/contradicted ·
👻 aspirational (exists only in docs/intent).

### Zen Garden → Koi (the deepest seam)

| # | Coupling | Mechanism | Evidence | Status |
|---|---|---|---|---|
| Z1 | Build dependency | **Five path deps to the sibling checkout** (`koi-embedded`, `koi-certmesh`, `koi-common`, `koi-crypto`, `koi-truststore`) — no crates.io versions | `zen-garden/Cargo.toml:98-102` | ✅ wired / ❌ as engineering (ZG's own critical-path defect #1: a clean clone cannot `cargo check`) |
| Z2 | Runtime embedding | Moss boots Koi **in-process**: `koi_embedded::Builder` with mdns+certmesh+http+udp+dashboard on; dns/health/**proxy off** | `zen-garden/src/moss/src/bootstrap/run.rs:566-607` | ✅ |
| Z3 | Discovery | All mDNS announce/browse goes through Koi (`mdns.register(koi_embedded::RegisterPayload{...})`, `MdnsEvent::Resolved/Removed`); lantern likewise | `zen-garden/src/moss/src/domain/discovery/mdns.rs:71-78,354-370` | ✅ |
| Z4 | Trust root | **Pond IS certmesh**: CA creation and TOTP enrollment delegate wholly to `koi_certmesh::CertmeshCore`; ZG runs no CA of its own; moss serves HTTPS from `{data_dir}/koi/certs/{stone}/` and refuses to start TLS without those certs | `zen-garden/src/moss/src/domain/security/pond_lifecycle.rs:81-133`, `api/v1/pond.rs:454-489`, `bootstrap/tls.rs:119-134` | ✅ |
| Z5 | Mutual TLS | Moss's server is `with_no_client_auth()` — "Phase 2: server TLS only. mTLS deferred to Phase 4." Client certs are presented only by rake | `zen-garden/src/moss/src/bootstrap/tls.rs:100-101`; `rake/src/main.rs:124-134` | 🟡 |
| Z6 | OS trust distribution | Rake installs the pond CA into the OS truststore via `koi_truststore::install_ca_cert(..., "zen-garden-pond")` | `zen-garden/src/rake/src/enrollment.rs:118` | ✅ |
| Z7 | Garden mesh (UDP 7184) | **ZG's own transport, not Koi's**: garden-common "owns ALL UDP communication" (multicast 239.255.42.99:7184); Koi's only role is opening the firewall port — and bridging *containers* into the mesh via koi-udp | `zen-garden/src/common/src/infra/communications/p2p.rs:1-26`, `constants/mod.rs:63`; `run.rs:582-587` | ✅ (commonly misattributed to Koi) |
| Z8 | Secrets | Moss re-exports `koi_crypto::vault::Vault` as its secrets store | `zen-garden/src/moss/src/infra/secrets.rs:14` | ✅ |

### Koi → Zen Garden (the substrate shaped by its consumer)

| # | Coupling | Mechanism | Evidence | Status |
|---|---|---|---|---|
| K1 | Demand-driven features | KOI-0001 is formally "**Depended On By: zen-garden ORCH-0001/2/3**"; koi-udp exists to carry ZG's `stone_chirp`/`tools_beacon` mesh messages into containers; embedded HTTP self-hosting (:5641) exists for the same consumer | `koi/docs/proposals/KOI-0001:7,21,72-78` | ✅ (declared in ADR; invisible in README/GUIDE) |
| K2 | Vocabulary leakage | "Non-Moss client (e.g. Rake on a workstation)" in the roster member-role enum; an entire `pond_ceremony.rs` module ("A pond is a private certificate authority for your garden"); TOTP issuer literally `"ZenGarden"`; koi-dns comments use a `.zengarden` zone; builder doc-comments cite "Moss discovery UDP" and "Zen Garden" firewall prefixes | `koi/crates/koi-certmesh/src/roster.rs:62-63`, `pond_ceremony.rs:122,543`, `koi-dns/src/aliases.rs:72`, `koi-embedded/src/lib.rs:198,215` | ❌ (product leaked into substrate) |
| K3 | Crypto constants | ZG vocabulary is baked into HKDF **domain-separation byte strings**: `b"pond-unlock-slot-totp-v1"`, `b"pond-fido2-storage-key-v1"` — functionally frozen (renaming breaks every existing vault) | `koi/crates/koi-crypto/src/unlock_slots.rs:41,551` | ✅ frozen (must be allowlisted, never "cleaned up") |
| K4 | Publication | koi-udp is **not on crates.io** and missing from the publish list; crates.io publishing silently broken since February; ZG's "all five deps are published" claim refers to possibly-stale Feb versions | `koi/findings/verification-2026-06.md` claim 4 | ❌ (blocks Z1's fix; staleness unverified) |

### Koan → Zen Garden (the framework consuming the satellite)

| # | Coupling | Mechanism | Evidence | Status |
|---|---|---|---|---|
| N1 | Mainline compile-time refs | **Five mainline projects reference ZenGarden**: Mongo, Ollama, Weaviate connectors → `Koan.ZenGarden.Core`; S3 → the **full** `Koan.ZenGarden` client; plus `Koan.AI.Connector.ZenGarden` | `koan-framework/src/Connectors/Data/Mongo/*.csproj:16`, `AI/Ollama:18`, `Vector/Weaviate:19`, `Storage/S3:12` | ✅ wired / ❌ as architecture (product-in-the-data-plane) |
| N2 | Hot-path resolution | `MongoOptionsConfigurator` parses `ZenGardenConnectionIntent` from the connection string and auto-resolves via `IZenGardenInitializationProvider` — **with autonomous fallback** (the one healthy coupling shape in the stack) | `koan-framework/src/Connectors/Data/Mongo/MongoOptionsConfigurator.cs:74-93` | ✅ |
| N3 | S3 hostage | The S3 provider is functionally a ZenGarden/Moss client — presign **throws** without a Moss endpoint; the bridge runs through a 119,907-byte single-file client class | Koan assessment `01-cartography.md:98-99,221-222` | ❌ |
| N4 | AI plane | `ZenGardenAiAdapter` routes **all nine** AI capabilities through ZG's AI Orchestrator at priority 0; Koan's Training/Eval facades can *only throw* — their sole providers live in the external ZG orchestrator | `koan-framework/src/Connectors/AI/ZenGarden/ZenGardenAiAdapter.cs:13-27`; Koan `01-cartography.md:206-209` | 🟡 (wired; sole-implementor risk) |
| N5 | Data seam (the product-shaped one) | ZG's MongoDB orchestrator (`ReplicaManager`, single authority) emits `mongodb://…?replicaSet=` strings over **`GET /api/cluster/connect`** — directly consumable by .NET | `zen-garden/src/orchestrators/mongodb/src/replica_manager.rs:50-51`, `api/cluster.rs:150-169` | ✅ |
| N6 | Shared semantics | A **cross-language URI test corpus**: "both implementations (Rust here, C# in Koan framework) MUST pass every case" | `zen-garden/src/common/tests/uri_corpus.rs:6` | ✅ (the healthy contract template) |
| N7 | Identity blur | Every ZG `Cargo.toml` declares `authors = ["Koan Framework"]`; ZG path conventions match `Koan.ZenGarden`'s resolver; a joint patent-analysis doc scopes both repos | `zen-garden/Cargo.toml:38`, `src/common/src/constants/paths.rs:164` | 🟡 |

### Koan → Koi (the thinnest seam)

| # | Coupling | Mechanism | Evidence | Status |
|---|---|---|---|---|
| KN1 | mDNS bridge client | `KoiHandler` (inside Koan.ZenGarden) probes Koi health and consumes its SSE stream for `_moss._tcp`/`_lantern._tcp`, against **hardcoded** endpoints `/v1/mdns/discover` + `/v1/mdns/subscribe`, over plain HTTP | `koan-framework/src/Koan.ZenGarden/Koi/KoiHandler.cs:5-8,86,117-118`, `Constants.cs:51-52` | ✅ thin |
| KN2 | Trust consumption | **Zero certificate-handling code exists in Koan src** (grep: X509, validation callbacks, truststore → no matches). A Koi-minted CA is honored only via the OS truststore (.NET default behavior); Koan offers no affordance for it. The only cert-adjacent code: a never-read `ValidateServerCertificate` flag and SqlServer's hardcoded `TrustServerCertificate=True` | verified grep over `koan-framework/src`; `ServiceAuthOptions.cs:24`, `SqlServerOptionsConfigurator.cs:173` | 👻 |
| KN3 | Discovery overlap | Koan carries **three** discovery stacks of its own (ServiceMesh UDP multicast, ZenGarden-Koi, Core.Orchestration candidates) — plus ZenGardenClient's raw multicast probe to ZG's 239.255.42.99 group, re-implementing mesh access in .NET | Koan `01-cartography.md:105-106`; `src/Koan.ZenGarden/ZenGardenClient.cs:2311-2325` | ❌ (duplication) |

### Closing the loop — what each assessment sees of the others

Zen Garden's six assessment documents mention Koan **zero** times. Koan's assessment mentions
Koi exactly **once** — as a duplicate-discovery problem. Koi's assessment names zen-garden
extensively but only as scope-creep provenance. **No document in any repo adjudicates the
stack**; the trilogy thesis exists in code wiring and in the architect's head, not in any
governed artifact. That absence is itself a finding: the Epic has no canon.

## 3. The revealed architecture

The dependency gradient matches the declared story — Koi depends on no sibling; Zen Garden
depends only on Koi; Koan consumes both — so the layering instinct is correct. What's wrong is
the **form** of every edge:

1. **Build-time where it should be runtime.** ZG consumes Koi as sibling path deps (Z1); Koan
   consumes ZG as in-tree ProjectReferences (N1). Both should be published-artifact or
   network-protocol edges. The one counterexample — N5's connection-string-over-HTTP — is the
   correct shape, and notably it is also the seam everyone agrees works best.
2. **Private where it should be versioned.** Endpoints are hardcoded (KN1); contracts live as
   in-tree types (N1); the only versioned, conformance-tested contract in the whole stack is
   the URI corpus (N6).
3. **Consumer names inside the substrate.** Koi carries its consumer's product vocabulary down
   to cryptographic constants (K2/K3). Names flowed downhill; the substrate cannot be offered
   honestly to anyone else while it speaks one customer's language.
4. **The trust column inverts the maturity gradient.** The layer the Epic is named for is the
   least built: Koi's proxy/revocation/CSR gaps + Z5's missing client-auth + KN2's zero cert
   code. Detailed in [02-synergy-audit.md](02-synergy-audit.md) §2.

## 4. One attention stream (verified)

Re-derived from all three git histories on 2026-06-11:

| Repo | Commits | Active days | Span | Monthly cadence | Public remote |
|---|---|---|---|---|---|
| koan-framework | 1,525 | 92 | since 2025-08-18 | 356·410·377·55 → **0×5 months** → 127·200 | NuGet stale at 0.8.x vs repo 0.17 |
| zen-garden | 1,272 | 84 | since 2026-01-24 | 192·265·344·334·100·37 (decaying) | stale since 2026-04-18; **zero tags ever** |
| koi | 176 | 20 | since 2026-02-07 | 113·61·0·2·0 | stale since 2026-03-26 |

Koan's *only* dormancy — 2025-11-05 → 2026-05-16, 192 days — is **exactly** the window in
which Zen Garden and Koi were built. The week Koan resumed (May 16), Koi effectively stopped
(2 commits since March) and Zen Garden began decaying (344 → 37/month). This is not three
parallel projects; it is **one strictly serialized attention stream rotating across three
repos, with two of three layers dormant at any moment as the observed steady state.** Total:
2,973 commits, ~490k lines, three languages, one author. Every stack-level plan in this
analysis must price that in — the Epic matures at roughly the *sum*, not the max, of the three
runways, and "dormancy-safe by construction" (tags, CI, pinned published deps) matters more
than any feature.
