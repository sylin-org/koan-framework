# E14 — The Zero-Egress Sovereign Lane (Sovereignty as a Tested Claim)

**Repo(s)**: Koan-led (profile + CI); composes Koi later · **Phase**: E · **Prereqs**: E06
(satellites; Koan works alone) · **One session** · Read [CHARTER.md](CHARTER.md) first.

## Mission

Turn "sovereign" from a vibe into a CI-verified claim: a defined **sovereign profile** (a
Koan app + its full local stack) that runs and passes its smoke tests inside a network that
**cannot reach the internet** — and a CI lane that proves it on every change. This
operationalizes the mission refusal "nothing in the sovereign path may require an account,
external service, or telemetry" and serves the audiences for whom offline-first is the
point (air-gapped, field, privacy-bound, or simply WAN-fragile environments).

## Context (verify each)

- Sovereign composition v1 is **Mongo + Ollama** (STACK-0001 item 9 — NOT Postgres/Weaviate;
  those were stubs).
- Koan's capability ladder is designed to scale down (in-memory → durable); the AOT probe is
  a separate Koan card (07-P5.1) — this prompt is about *egress*, not AOT; don't conflate.
- Likely egress offenders to hunt: package/feed touches at runtime, telemetry defaults
  (Koan.Core bundles OpenTelemetry — verify it does not export by default), model pulls
  (Ollama needs its model **pre-provisioned**), version checks, container-image pulls at
  runtime.

## DECIDED

1. **The profile is a compose file** (`KOAN/samples/` next to existing dogfoods, name it
   for what it is, e.g. `S-sovereign/`): Koan sample app + MongoDB + Ollama (model baked or
   volume-provisioned ahead of time), all attached **only** to a Docker network with
   `internal: true` (no default route). No other network.
2. **Smoke = real behavior, not liveness**: entity CRUD over REST, one embedding/semantic
   call through local Ollama, the MCP capability endpoint, and the E12 self-description
   endpoint — all green with zero egress possible.
3. **The lane fails on egress *need*, and detects egress *attempts***: (a) the
   internal-only network makes any required egress fail the smoke (the hard gate);
   (b) additionally log/observe attempted outbound connections (e.g. a sidecar/tcpdump or
   compose-level observation as available on the runner) and report attempts even when
   nonfatal — attempted-but-tolerated egress is telemetry debt to file.
4. **Model provisioning is part of the recipe**: the docs show exactly how the Ollama model
   gets there without runtime internet (pre-pull into a volume / baked image), because
   that's the real-world air-gap question.
5. **A `docs/guides/sovereign-deployment.md` section** (create or extend per Koan's docs
   layout) documents the profile, the guarantees the lane enforces, and what is explicitly
   out of scope (e.g. OS/package updates).
6. CI: the lane runs on the Koan repo (workflow job bringing up the compose on the runner,
   running smoke, tearing down). If runner constraints block compose, implement the
   closest runnable equivalent and document the gap precisely — no green-by-skipping.

## DEFAULT

- Sample app: reuse an existing small sample with `[McpEntity]` + `[Embedding]` usage
  rather than writing new (verify snippets against src — the repo's snippet-truth rule).
- Anything found phoning home by default gets fixed at the root (config default flipped or
  the dependency gated) — per the no-stopgaps rule; if the fix is out of scope, file it
  precisely and make the lane's expectation explicit.

## Plan of record

1. Inventory runtime egress paths (grep for HttpClient construction in startup paths,
telemetry exporters, model pull logic). 2. Build the compose profile with internal-only
network + provisioning recipe. 3. Write the smoke script. 4. Run; fix root causes of any
egress need found. 5. Wire the CI job. 6. Docs + SURFACES.md ("sovereign profile → guard:
zero-egress lane"). 7. Report attempted-egress findings.

## Verification

The profile passes smoke with `internal: true`; flip the network to normal once and confirm
the smoke is identical (the profile isn't accidentally depending on the isolation);
deliberately add an egress call in a scratch branch and confirm the lane fails (mutation
check), then discard.

## Definition of done

- [ ] Sovereign profile committed; smoke covers CRUD + local AI + MCP + self-description.
- [ ] CI lane green on internal-only network; mutation check performed.
- [ ] Provisioning recipe documented; egress findings fixed at root or filed precisely.
- [ ] SURFACES.md updated.
