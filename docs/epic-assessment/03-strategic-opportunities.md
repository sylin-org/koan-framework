# 03 — Strategic Opportunities at Stack Level

What the combination can claim that no single layer can — compared against the 2026 landscape
(Tailscale + Services, k8s/k3s, CasaOS/Umbrel/Uncloud/Coolify/Komodo/Dokploy, GPUStack, exo,
cloud PaaS, Supabase, the MCP ecosystem, Home Assistant). Ranked by **uniqueness ×
feasibility given that all three projects are pre-launch**. Sources: the two landscape
research corpora (koi `research/`, zen-garden `landscape-data.md`) plus code-verified seams.

## §0.0 The posture: enablers, not competitors (Epic doctrine)

The mission frame (README) governs how every opportunity below is read. The stack exists to
**capacitate** — to put self-hosting, compute sovereignty, and production-grade capability
within reach of individuals and small teams usually denied them — and to do it as an
**enabler of the tools people already run**, never as their replacement. Koi's assessment
already wrote the rules of engagement; they are hereby Epic-wide canon, applied to all three
layers:

1. **Export in their formats; never require import in ours.** (Koi: ACME to proxies, http_sd
   to Prometheus, RFC 2136 to DNS servers, split-DNS to tailnets. ZG: standard connection
   strings, standard containers. Koan: REST+OpenAPI, SSE, MCP — never a proprietary client.)
2. **Consume what users already wrote.** Existing traefik/caddy labels, existing compose
   files, existing CAs, existing models.
3. **Be the substrate, not the surface.** The user's dashboard stays Grafana/Homepage; the
   user's proxy stays Caddy; the user's editor agent stays whatever they chose. The stack
   supplies knowledge, trust, placement, and grammar underneath.
4. **Every capability needs an exit.** Easy to stop using is the precondition for easy to
   start using: certs that outlive Koi adoption, databases reachable without the
   orchestrator, entities that are plain code. Sovereignty includes sovereignty *from us*.
5. **Degrade gracefully when a layer is owned.** Detect the incumbent and offer the feeder
   posture instead of double-serving.

"Landscape" analysis below therefore locates **unserved people**, not territory to win. Where
an incumbent serves its users well (Tailscale's overlay, Pi-hole's ad-blocking, Syncthing's
sync), the stack docks with it and feeds it. The gaps the Epic fills are the ones where the
capability is *denied* — account-walled, enterprise-priced, cloud-only, or simply absent.

The structural insight: **every local-first incumbent covers exactly one plane.**
Tailscale covers the overlay (Services GA proves service-identity demand — at the
account-bound layer); k3s/Uncloud/Coolify/Komodo cover human-decided container placement;
GPUStack covers GPU scheduling; CasaOS/Umbrel cover the single-node app store; Supabase
covers the cloud data plane; Home Assistant covers IoT. **Nobody covers two planes** — so an
individual assembling sovereign capability today must integrate five products and still ends
up with no trust fabric and no application grammar. The Epic is the only assembly where four
planes exist under one roof and are already code-wired; its job is to close that integration
gap *for* people, while staying pluggable at every plane per the doctrine above. The
liability is that vertical integration is also the scope-death pattern (arkOS, Sandstorm) —
so everything below is sequenced as *wedges that stand alone*, with the vertical as the
composed end state, never the launch unit.

## §0 Five cross-project conflicts to resolve before any opportunity

These cost decisions, not engineering — uniquely cheap here because all three repos share one
architect. Unresolved, they undermine every item below.

1. **MCP layering doctrine.** Koi's #1 opportunity and Koan's MCP pillar claim the same agent
   interface. Resolution is layering, not arbitration: **Koi = network-substrate MCP**
   (discover/resolve, DNS, certs, health — and discovery *of other* MCP servers via
   `_mcp._tcp`); **Koan = application MCP** (entity tools + governance). Koi advertises Koan
   endpoints; it never wraps them.
2. **Discovery doctrine.** ~6 mechanisms coexist across the stack (01 §2, KN3/Z7). Declare:
   Koi is the sole LAN naming/mDNS authority; the UDP-7184 garden mesh is ZG-internal gossip,
   never a cross-project contract; Koan keeps only its candidate pipeline, into which siblings
   plug as satellite sources. ([04 §R7](04-architecture-alignment.md) has the mechanics.)
3. **The AI succession, decided jointly.** ZG cannot archive the `ai` crate while it is the
   designated endpoint of `Koan.AI.ZenGarden` and the only implementor of Koan's Training/Eval
   surface. One joint ADR: pick the `ollama` orchestrator now (the deployed generation),
   define the single-endpoint contract on *it*, harvest the `ai` crate's designs, archive the
   rest — and trim Koan's Training/Eval facades to match (Koan's own MLOps shed already points
   there).
4. **The sovereign composition, made honest.** Anchor v1 on **Mongo + Ollama** (the two real
   orchestrators). Either build real Postgres/Weaviate choreography later or stop naming them
   in the sovereign profile. Delete the stubs (ZG's shed register already orders this).
5. **Coupling form.** Ratify "works alone, lights up together" (the N2 fallback pattern) as
   the only permitted coupling shape, and publish the four seam contracts as versioned APIs
   ([04 §R2](04-architecture-alignment.md)).

## §1 The Agent-Ready LAN — Koi MCP discovery × Koan MCP governance

**The claim nobody else can make**: *an agent on your LAN can discover a named, TLS-trusted,
permission-scoped tool surface — and the operator can audit and revoke it.* The MCP ecosystem
has two documented unsolved problems at the local level, and each sibling independently owns
exactly one: Koi solves **where** (LAN MCP discovery is unserved — the community port-scans
localhost; the one incumbent is a weak Python package; the MCP spec's mandated DNS-rebinding
protections are precisely what named, certified local endpoints provide), Koan solves **who
and how much** (MCP's security norm is all-or-nothing bearer tokens; grants/audit/revocation
over `[McpEntity]` is Koan's accepted capability #2). Docker's MCP Catalog is a registry, not
LAN discovery; MCP gateways are crowded but process/cloud-scoped; Tailscale has connectivity
without MCP semantics; hass-mcp is domain-bound.

**Why it's ranked first**: it is the only stack play where **Zen Garden is not on the critical
path**. Koan's MCP endpoints are shipped and auto-mapped today; koi-mcp is specced (Koi prompt
P11) as a thin wrapper over the existing OpenAPI surface. The composed demo — Koi advertises a
Koan app's MCP endpoint via `_mcp._tcp` with a certmesh cert; an agent finds it by name and
operates it under a scoped grant — is weeks of glue.

**Prerequisites**: conflict #1's doctrine; Koi's two contract bugs (per-boot token → every
documented POST 401s; loopback-only bind) — they break programmatic consumers, which is what
agents are. **Risks**: a `.well-known`/registry convention could standardize before `_mcp._tcp`
wins — support both; security-flavored marketing invites scrutiny the trust layer can't yet
survive (§2 first or simultaneously).

## §2 One trust fabric, metal to agent call — layered, with mutual hole-patching

Workload identity at LAN scale is owned by nobody: SPIFFE/Consul are enterprise (and on all
three shed lists), mkcert is dead, step-ca is heavy and has no discovery/DNS, Tailscale certs
are account-bound and CT-logged. The `.internal` TLD reservation and the 47-day public-cert
mandate make private CAs structurally necessary. The Epic has the only end-to-end spine in
the space — certmesh TOTP ceremony → 30-day auto-renewed certs → OS truststore install →
fleet identity (pond) → Koan Security.Trust (its best-engineered small project) with planned
KSVID + coherence-epoch revocation.

**The stack-level insight no single assessment could state**: each layer's flagship weakness
is compensated by another layer's mechanism. Certmesh revocation is roster-only (revoked
certs stay valid up to 30 days) — Koan's coherence-epoch revocation cuts application-layer
access in seconds: the compensating control. Conversely, Koan has zero certificate code —
certmesh + koi-truststore is the cryptographic root it lacks, working today via the OS store
with no .NET code change. **Two fabrics, one binding, never merged**
([04 §R10](04-architecture-alignment.md)).

**Prerequisites (honesty repairs before any security marketing)**: certmesh CSR-based
enrollment (keys that traveled are not proof-of-possession anchors); moss client-auth
(Phase 4); ZG's :7185/`/deploy` holes closed; revocation semantics documented, not
discovered. **Risk**: PKI trust is reputation-bound — a young, solo, pre-launch project
asking to be your CA is the hardest sell in the portfolio. The fact pack found the gaps in
days; reviewers will too. Scope claims to enrolled machines; never market around the
unmanaged-device problem.

## §3 The Windows-10 exit wave — Zen Garden leads, the stack supplies the third act

The only **dated** hook in the landscape: consumer ESU ends **2026-10-13**; ~400M capable
machines decide their fate once, in Q3/Q4 2026; CasaOS (the natural destination, 34k stars)
has been dormant since Aug 2025; the real competition is Linux Mint and inertia. ZG alone
lands as "an orchestrator with nothing to orchestrate" — fleets, names, and certs are means.
The stack supplies the outcome: *wipe two old laptops and a phone → they become a garden with
names and TLS (Koi rides invisibly inside Moss — it needs no separate launch) → one agent
session later, a working AI app with a self-healing replicated database is serving your
family.* No fleet tool offers an application outcome; no app framework offers the hardware.
The pull-the-plug demo (replica heals **and the app stays up**) is the claim
Komodo/Dokploy/Uncloud architecturally cannot copy.

**Prerequisites**: ZG's own critical path 1–5 inside ~3 months (clean clone, merge the June
branch, first CI, first tagged release, close the security holes) + one tested
"laptop-to-stone" path + **one pre-built Koan sample offering** (Mongo+Ollama per conflict
#4 — Koan does not formally launch; it supplies demo content). **Risk**: the deadline is real
and release engineering is all three projects' weakest dimension — see the go/no-go rule in
[05-leverage-plan.md](05-leverage-plan.md) §4.

## §4 Self-sovereign BaaS — "Supabase you can own," data plane first

Supabase's DX is the most-loved in app development; its structural ceiling is that BaaS can't
ship on your hardware (Koan's own assessment names this as the exploit behind capability #5).
The Epic already implements the sovereign equivalent **for one database, end-to-end, today**:
ZG choreographs and heals the replica set and serves connection strings over
`/api/cluster/connect`; Koan's connector auto-resolves them; entities become REST + MCP APIs
with auth; Koi names and certs every endpoint. BaaS DX where the "service" is your own
scavenged fleet. k3s makes you assemble this from YAML; Coolify/Dokploy deploy containers but
neither heal them nor provide an app framework; Uncloud's 2026 roadmap (first-class
databases) proves the demand and bounds the window (~18 months).

Read through the mission frame, this is not an anti-Supabase play: it serves the people
Supabase structurally cannot — data-residency-bound, air-gapped, billing-averse, or simply
owning hardware that deserves a second life. The enabler posture applies inward too
(doctrine #4): the database remains a standard MongoDB reachable by any client; leaving the
stack costs a connection string.

**Prerequisites**: conflicts #3/#4 resolved; the seam contracts published; one filmed
end-to-end demo on real mixed hardware. **Risks**: MongoDB's SSPL is awkward optics in a
sovereignty pitch and "where's Postgres?" is the first question a Supabase comparison
invites — answer honestly (roadmap, not stubs); the .NET-only app layer narrows the audience.

## §5 Governed agent operations of physical infrastructure

People already wire agents to their labs (the "I manage my homelab by talking to it" genre;
hass-mcp at 2M+ HA installs) — and every current path hands the agent an unscoped bearer
token to things that can reboot machines and delete data. The Epic uniquely combines **verbs
genuinely worth governing** (ZG: deploy/reboot/placement/snapshots; Koi: DNS records, cert
provisioning; Koan: jobs/cache/data verbs) with **the only governance layer in the space**
(capability #2 extended to ops verbs — capability #7). The narrative jiu-jitsu: ZG's worst
security hole (unauthenticated reboot/deploy on :7185) gets closed **by** the trust fabric —
the same change that fixes the disqualifying defect ships the differentiating product. MCP
gateways (Docker, Microsoft, Obot) cannot reach this position: they don't own the metal or
the CA.

**Prerequisites**: §1 + §2; a conservative launch verb set (read-mostly; explicit grants for
destructive ops); a real append-only audit trail on the Koan side (certmesh already has one).
**Risk**: one agent-with-root incident burns all three brands at once.

## §6 The composition audit — lockfile ↔ manifest ↔ roster

Koan's planned composition lockfile (behavioral SBOM), ZG's garden manifest/observe, and
Koi's roster/status are the same instinct — *the system states what it is* — over three
domains. Standardized as an envelope convention ([04 §R9](04-architecture-alignment.md)),
they compose into a runtime audit nothing else on the market offers: **diff what was shipped
(lockfile) against what is placed (manifest) against what is trusted (roster)** — drift
detection across application, fleet, and PKI in one query, and a uniform agent-introspection
surface across all three layers. Cheap (each piece is already planned per-repo), uniquely
stack-shaped, and the operational half of Koan's agent-native thesis.

## §7 North star — the Sovereign Agentic PaaS (named early, marketed last)

The composed end state: *one agent session → a running, named, TLS-trusted, AI-enabled app on
hardware you own — no account, no YAML.* Cloud PaaS sells exactly this integration, on their
hardware, under their accounts. Maximal uniqueness, lowest immediate feasibility: it requires
all three projects at release-grade simultaneously, which is precisely what the serialized
attention stream cannot deliver soon. **Sequence it as the composed result of §§1–5, never as
a launch target.** Name it early (it makes the wedges coherent); market it only after the
minimal truth set ([05](05-leverage-plan.md) §1) is green. The survivor pattern (Syncthing's
narrow scope, Coolify's build-in-public) argues for shipping wedges and letting the vertical
*emerge*.

## §8 Refused lanes at stack level

The union of the three shed lists, plus stack-specific refusals:

- **No mono-repo, no shared release train** — it would couple three unfinished release
  engines and institutionalize the path-dep disease; each repo must be releasable alone first.
- **No shared trust library across the language split; no ports** (no Koi-in-.NET, no shared
  Rust/C# crypto, no ZG rewrite). The split is healthy as a protocol boundary, fatal as a
  shared-codebase ambition for one maintainer.
- **No merged trust fabric** (certmesh ⊄ KSVID, KSVID ⊄ certmesh) — layered with a binding
  convention only ([04 §R10](04-architecture-alignment.md)).
- **No parallel MCP pushes** — sequence Koi's discovery substrate and Koan's governance; two
  half-shipped MCP surfaces from one author is the serialized-attention failure mode.
- **No WAN/federation/overlay** (Tailscale owns it; Back to My Mac died there), **no
  enterprise PKI/SPIFFE/compliance** (Koi's shed list: "the compliance endpoint is the first
  step down this road — delete it"), **no k8s backend ambitions**, **no model
  sharding/MLOps** (exo owns sharding; the claim is placement), **no app-store sprawl**, plus
  each repo's own refused lanes (Koan: CRDTs/workflow/UI scaffolding; ZG: PaaS-from-git,
  storage/NAS depth; Koi: ad-block DNS, proxy feature parity, tunneling).
- **No marketing the Epic before the minimal truth set is green** — three broken front doors
  in sequence is negative advertising with a multiplicative funnel.
- **Mission-derived refusals**: nothing in the sovereign path may require an account, an
  external service, or telemetry to function (offline-first is a load-bearing differentiator,
  not a feature flag); no capability ships without its exit (doctrine #4); no
  replace-the-incumbent posture where a feeder posture serves the user better — if a play
  only makes sense as displacement, it is off-mission regardless of how winnable it looks.
