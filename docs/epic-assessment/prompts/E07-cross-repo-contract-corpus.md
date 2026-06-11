# E07 — The Cross-Repo Contract Corpus

**Repo(s)**: all three · **Phase**: B · **Prereqs**: E03+E04 (published deps; CI exists in
ZG), E05 (stable Koi API) · **One session** · Read [CHARTER.md](CHARTER.md) first.

## Mission

Generalize the one healthy cross-language contract pattern in the stack — the shared URI
test corpus — to the actual integration seams, so that integration truth stops living only
on the maintainer's machine and survives lane rotation. Fixtures are owned by the layer that
owns the semantics, published as artifacts, and consumed as tests by the other side's CI
**against pinned released artifacts, never sibling checkouts**.

## Context (verify each)

- The template: `ZEN/src/common/tests/uri_corpus.rs:6` — "both implementations (Rust here,
  C# in Koan framework) MUST pass every case." Find the C# consumer in Koan (grep for the
  corpus file/cases) to see the consumption shape.
- The seams to cover (STACK-0001 item 2):
  1. **Offering resolution** (ZG owns): garden connection-intent strings → expected
     resolution behavior (consumed by Koan's satellite from E06).
  2. **Cluster connect** (ZG owns): `GET /api/cluster/connect` response shape —
     `ZEN/src/orchestrators/mongodb/api/cluster.rs:150-169` returns
     `{"connections":[{"connection_string": "mongodb://…?replicaSet=…"}]}`.
  3. **mDNS bridge** (Koi owns): `/v1/mdns/discover` + `/v1/mdns/subscribe` request/response
     + SSE event shapes (consumed by Koan's `KoiHandler` and ZG).
  4. **certmesh enrollment** (Koi owns): join/renew request/response wire shapes
     (`KOI/crates/koi-certmesh/src/http.rs:40-107`) — consumed by ZG's pond.
- Cross-repo CI reality: ZG just got CI (E04); Koan's CI state is weak (its own Track B
  fixes it) — wire what can run now, leave precise TODOs wired to SURFACES.md otherwise.

## DECIDED

1. **Fixture form**: JSON files (cases + expected), versioned, living in the owning repo
   under a `contracts/` directory (e.g. `ZEN/contracts/cluster-connect/v1/*.json`,
   `KOI/contracts/mdns-bridge/v1/*.json`), shipped with releases.
2. **Consumption form**: each consumer repo vendors a pinned copy (with the source version
   recorded) and runs a test that replays the cases against its own implementation
   (serializer round-trip + behavioral assertions where cheap). A small
   `contracts/UPSTREAM.lock`-style note records the pinned upstream version.
3. **Live-seam jobs where infrastructure allows**: ZG CI builds against published koi
   crates (E04's gate already does this); a Koan integration lane spins the released Koi
   binary and asserts `/v1/mdns/discover` against the fixture shapes. (The released-ZG-
   container lane lands when ZG has releases — leave the job skeleton + skip-with-reason.)
4. Schema-only is acceptable for v1 (shape over semantics) — semantics deepen in E11/E12.
   Do not invent semantics the code doesn't have; fixtures encode **current verified
   behavior**.

## DEFAULT

- Fixture generation: derive initial cases by exercising the real endpoints/parsers locally
  and capturing actual outputs (truth-first), then minimally curating.
- 5–15 cases per seam for v1.

## Plan of record

1. Study the URI corpus producer/consumer pair end to end. 2. For each seam: capture real
shapes → write fixtures in the owning repo → write the consumer test in the consuming repo →
pin the version. 3. Wire CI steps that exist-able today; skeleton + skip-reason for the
rest. 4. SURFACES.md rows in all three repos ("seam: X → guard: contract test Y"). 5. Docs:
a short `contracts/README.md` per owning repo explaining the pattern and the ownership rule.

## Verification

All new contract tests pass locally in their repos; deliberately corrupt one fixture field
and confirm the consumer test fails (the corpus actually bites); restore.

## Definition of done

- [ ] Four seams have v1 fixtures in their owning repos + passing consumer tests.
- [ ] Pinning recorded; CI wired where runnable, skeletons elsewhere with reasons.
- [ ] Mutation check performed (corrupt → red → restore → green).
- [ ] SURFACES.md updated in all three repos.
