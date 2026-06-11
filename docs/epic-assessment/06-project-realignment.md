# 06 — Project Realignment Through the Epic and the Mission

How each project's identity, priorities, and shed lists shift once three lenses are applied
together: the Epic findings (01–05), the mission frame (README — capacitation, compute
sovereignty, enablers-not-competitors), and the corrected operating model (README Corrections
— a single serial lane that matures surfaces by dogfooding them inside downstream solutions,
some private). Per-project assessments remain authoritative for their internals; this
document only adjusts *framing and ordering*, and adds the mission-aligned opportunities the
per-project work could not see.

## §1 Koi — from "LAN toolbox" to "the layer that makes trust affordable"

**Identity realignment.** Koi's assessment already discovered that "the wiring is the
product" and proposed the feeder posture; the mission frame makes that the *primary*
identity, not a fallback: Koi is the substrate that gives people a capability today reserved
for those with PKI expertise or platform budgets — **names, certificates, and discovery that
just exist on a network you own**. Its two customers are now both honest and public: the
human self-hoster, and sibling/downstream solutions consuming it programmatically
([04 R5](04-architecture-alignment.md)).

**Reassessment delta from the correction.** "What is easy to test is exquisitely tested; what
is risky has plausibly never been run" becomes: *what the lane is on is well-verified; what
the lane left behind without a guard rotted silently.* The TLS plane worked, regressed at the
axum 0.8 upgrade while unexercised, and nothing mechanical noticed. Same severity today,
different prescription: Koi's problem is not craft or even verification capacity — it is
**guard placement at rotation time** (§5).

**Mission-ordered priorities.**
1. Truth restoration (its Stage 0) — unchanged, but now mission-critical: the capacitated
   audience cannot debug a fictional front door.
2. **The two programmatic-consumer bugs first among fixes** (per-boot token, loopback bind):
   under the enabler doctrine, scripts, agents, and sibling solutions are first-class users,
   and both bugs specifically break *them*.
3. **Publish the crate closure** (incl. koi-udp) — unblocks Zen Garden's entire critical
   path and is itself doctrine #4 (an exit from sibling-checkout coupling).
4. **`.internal` easy-button, promoted.** The purest denied-capability play in the whole
   portfolio: ICANN reserved `.internal`, public CAs are forbidden to issue for it, so
   warning-free TLS on the sanctioned private TLD is *only* achievable with exactly Koi's
   certmesh+dns+truststore trio — today gated behind PKI expertise nobody in the mission
   audience has. Mostly positioning and docs.
5. **`koi trust` (generic truststore), promoted** — peak enabler: root-distribution for *any*
   CA (step-ca, Caddy, mkcert, corporate), making Koi useful to people who keep their
   existing CA. 306 lines already exist; this is CLI surface + docs.
6. The ACME facade remains the strategic build (the proxies people already run obtain certs
   from Koi with one config line — enablement in its strongest form), and koi-mcp follows the
   Epic's §1 sequencing.

**Shed harder under the mission**: the compliance endpoint (already ordered deleted — it
serves an enterprise persona the mission doesn't); proxy-as-product stays
excluded-until-tested ([04 R6](04-architecture-alignment.md)) — when the lane next exercises
it through a real downstream solution, the data-plane tests get extracted *from that
exercise* and left behind as the guard.

## §2 Zen Garden — from "fleet orchestrator" to "hardware reclamation as capacitation"

**Identity realignment.** Zen Garden is the mission-purest project of the three: it converts
the most widely-denied capability gap — perfectly good hardware deemed worthless — into
compute sovereignty. Through this lens the Win10-ESU date is not a market window but a
**mission moment**: ~400M machines are about to be *denied* security capability, and their
owners will decide, once, between the landfill and a second life. The Android stone is the
sharpest expression of the identity — phones are the most-owned computers on earth and the
most categorically denied a server role.

**Mission-ordered priorities.** Unchanged in content from its own critical path, re-ordered
in *meaning*: the L0 distribution gap is a mission failure before it is an engineering one
(first-time builders are the persona most harmed by fiction); the unauthenticated :7185/
`/deploy` holes are disqualifying for a duty-of-care audience specifically; the honest
"wipe this laptop, become a stone" path is the product. The autonomy demo (pull the plug,
the app stays up) is the capacitation proof: *resilience without a platform team.*

**Shed harder**: everything its register already orders — and the mission adds a test for
future temptation: catalog breadth, storage depth, and provider matrices serve a *platform*
persona; capacitation is served by the narrow loop (discover → place → heal → name) working
flawlessly on donated hardware.

## §3 Koan — from "agent-native framework" to "democratizing software production"

**Identity realignment.** Koan's accepted thesis (the scarce resource is agent-loop
iterations) gains its mission reading: what is being democratized is **software production
itself** — a small senior team plus agents shipping what used to require a platform
department. Koan's distinctive obligation in that world is *making agent labor trustworthy*:
the lockfile (what is this system?), conformance kits (does it behave?), governed access (who
may do what?), fail-loud composition (no silent lies). The Epic adds the substrate those
guarantees stand on (Koi trust, ZG placement) — and the mission adds the constraint that
none of it may require an account, a cloud, or a Claude-shaped agent: MCP/OpenAPI keep the
agent surface harness-neutral.

**Mission-ordered priorities.** The existing 06/07 stash ordering survives intact (truth →
enforcement → second-act capabilities); the Epic inserts the satellite inversion track
([05 §3](05-leverage-plan.md)) so Koan *works alone* — which is the enabler doctrine applied
to itself — and conflict #4 re-anchors the sovereign profile on Mongo+Ollama so the mission
flagship ("one box, no accounts, air-gapped") is honest. The "exit" audit is worth one
explicit pass: entities are plain code, data lives in standard stores, APIs are standard
HTTP — leaving Koan must cost a refactor, not a rewrite. That claim is currently true and
should be stated and tested, because it is rare.

## §4 Mission-aligned opportunities the per-project assessments missed

Mapped to owning project; none requires new invention beyond what 03 already sequences.

1. **The accidental sysadmin** *(stack-wide persona, unowned by anyone in the market)*. Koi's
   spec already names "small orgs with a duty of care but no IT department" — community
   centers, clinics, schools, nonprofits, co-ops. Nobody serves them: enterprise tooling
   assumes staff, homelab tooling assumes hobby time. The stack's zero-config posture is
   *built* for them, but no preset profile, no guide, no vocabulary addresses them. Cheap:
   one documented profile per layer ("the community-center setup"), conservative defaults,
   duty-of-care security posture. This persona is the mission's center of gravity and would
   sharpen a hundred small decisions.
2. **The community GPU pool** *(ZG-led, Koan AI-consumed)*. ZG's VRAM-aware placement across
   mixed scavenged GPUs, read as mission: people priced out of AI subscriptions and out of
   single big-GPU builds pool several modest cards nobody could use alone — "AI without the
   subscription, on hardware saved from the landfill." GPUStack serves enterprises; exo
   shards one model; *pooling-for-access at community scale* is unclaimed and is the
   mission-native framing of ZG's existing opportunity #4.
3. **The repair-café / classroom channel** *(ZG-led distribution, not engineering)*.
   Right-to-repair laws are live in four US states; repair cafés and school labs are exactly
   where Win10-exit hardware will physically pile up. Docs-as-curriculum ("donated hardware →
   community lab," teacher-shaped), and presence in those communities, is distribution
   through mission alignment rather than marketing — and it composes with #1.
4. **Offline-first as a *tested* Epic profile** *(stack-wide)*. Koi's research already names
   the segment (ships, field deployments, OT networks, privacy-required orgs) and the stack
   is accidentally good at it (no SaaS account, no public domain, no CT logs, local AI, local
   data). Make it deliberate: one CI lane that runs the sovereign profile with **zero
   egress** and fails on any outbound attempt. Sovereignty that is tested is a claim;
   untested it is a vibe. This also operationalizes the §8 mission refusal ("nothing in the
   sovereign path may require an account, external service, or telemetry").
5. **Software that shows its work** *(Koan-led, stack-wide)*. The portfolio's biggest social
   liability — solo + AI-amplified volume, now a community trust tax (the Booklore failure
   mode; Jellyfin's burnout statement) — is answerable by machinery the stack is already
   building for other reasons: the R9 self-description envelope, the behavioral-SBOM
   lockfile, executable docs, disclosed AI-methodology as a written reviewable practice, ZG's
   honest decision culture. Packaged together and *named*, this is a trust feature no
   incumbent ships: provenance and verifiability as product, for software produced the way
   all software is about to be produced. It converts the liability into the differentiator.
6. **Data dignity, composed not built** *(Koan+ZG, later)*. "Your family's photos, documents,
   and AI conversations under your own roof" is the consumer-legible form of the sovereignty
   story — reachable by *composition* (Koan entities + media + ZG-choreographed Mongo + Koi
   names/TLS) without building any of the storage/NAS depth the shed lists forbid. A sample
   app and a narrative, not a product line. Sequenced after the truth set; noted so the
   refusal of NAS-depth isn't mistaken for refusal of the story.

## §5 The process realignment: solution-driven maturation, formalized

The correction reveals the real maturation engine: **surfaces mature when a downstream
solution exercises them; they rot when the lane departs without leaving a guard.** Rather
than fight the model (the lane is singular and that won't change), give it mechanical memory:

1. **The surface ledger** (per repo, one small table, kept in-repo): *surface → exercising
   solution → last-exercised date → guard.* Private downstream solutions appear under a
   neutral label ("private downstream solution") — the ledger needs the *fact* of exercise,
   never the name. The Koi TLS plane's row would have read "proxy → private downstream →
   2026-0X → none," and the regression would have been a known risk instead of a discovered
   fiction.
2. **The rotation contract** (the departure checklist, enforced by habit + CI): before the
   lane leaves a repo or surface — tag it; CI green; a tripwire test exists for every surface
   the departing solution was exercising (extracted from that exercise, however minimal);
   `status()`-class endpoints tell the truth; the ledger row is updated. *Leave a guard at
   the door when you leave the room.*
3. **The assessment convention**: future audits read an unguarded surface as "unexercised
   since \<date\>," not "never worked," and consult the ledger before dating any claim. This
   keeps honesty bidirectional — the docs neither over-claim function nor over-claim failure.

This is the smallest process change that makes the serial-lane model — which produced three
real systems in ten months — *compatible* with the truth-first, dormancy-safe posture the
whole Epic plan rests on.
