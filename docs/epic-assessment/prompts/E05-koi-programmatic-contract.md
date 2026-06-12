# E05 — Koi Works for Programs (Token Story, Then Bind)

**Repo(s)**: Koi · **Phase**: B · **Prereqs**: none · **One session**
Read [CHARTER.md](CHARTER.md) in full first.

## Mission

Fix the two verified contract bugs that break every *programmatic* consumer of Koi — agents,
sibling solutions, scripts — and document the networking/security model so the fixes are
discoverable. Under the enabler doctrine these consumers are first-class: an API-first
substrate whose documented write examples all 401 and whose daemon can't be reached from
containers is broken at its identity, not at a feature.

**Order is mandatory: token story first, bind flag second.** Opening the bind address before
the auth story widens the attack surface on a daemon whose mutations are gated by a value
only the daemon process knows.

## Context (verify each)

- **Bug 1 (auth)**: every non-GET endpoint requires an `x-koi-token` DAT regenerated per
  daemon start; README/CONTAINERS/http-api.md show tokenless POSTs; http-api.md never
  mentions auth → every documented write example returns 401
  (`KOI/docs/assessment/findings/verification-2026-06.md` claim 3).
- **Bug 2 (bind)**: the HTTP adapter binds `127.0.0.1` only, no bind flag exists;
  CONTAINERS.md promises bridge-gateway access → the headline container story fails on
  native Linux Docker (claim 2).
- Related known issue to fold in: `--endpoint` sends an empty/local token to remote
  endpoints with no `--token`/`KOI_TOKEN` escape hatch (claim 10), and Koi's own assessment
  orders a token-topology fix (child tokens so an embedded consumer's `shutdown()` can't
  kill the daemon).

## DECIDED

1. **Stable token provisioning for programmatic consumers**: on first start the daemon
   persists a token file at a fixed, documented, permission-restricted path under its data
   dir; `--token <value>` and `KOI_TOKEN` env are honored by both daemon (override) and CLI/
   client (presentation); `--endpoint` + `--token`/`KOI_TOKEN` work for remote daemons. The
   per-boot ephemeral default goes away in favor of the persisted token (regenerate via an
   explicit `koi token rotate`).
2. **`--http-bind <addr>` flag** (+ config equivalent): default stays `127.0.0.1`; opting
   into `0.0.0.0` or a bridge IP **requires** token auth active (refuse to bind non-loopback
   if auth is somehow disabled).
3. **One "Networking & security model" reference page** (`docs/reference/` per Koi's doc
   layout): bind addresses, token lifecycle (file path, rotation, env/flag), the mTLS
   inter-node port, CORS posture, container access recipe — and README/GUIDE/CONTAINERS/
   http-api.md all *reference* it instead of restating (Koi's Stage-0 already orders this
   page; write it here since these fixes define its content).
4. **Every previously-401 documented example is updated to pass** (with the token shown) —
   and at least the core write examples become executable doc-tests/CI checks (guard).
5. `/v1/mdns/*` and `/v1/certmesh/*` are marked **v1-stable** in the OpenAPI/utoipa
   annotations and the reference docs (the Epic contract surface — STACK-0001 item 7).

## DEFAULT

- Token file name/location: follow Koi's existing data-dir conventions; 0600 perms (and the
  Windows ACL equivalent).
- Implement the child-token topology fix here if it falls out naturally of the token
  refactor; otherwise leave a precise TODO + SURFACES note for Koi's own stash.

## Plan of record

1. Map the current token creation/validation code (grep `x-koi-token`) and the HTTP
adapter's bind site (`TcpListener::bind`). 2. Implement token persistence + flag/env
plumbing + rotate command. 3. Implement `--http-bind` with the auth-required refusal.
4. Write the reference page; fix all affected examples. 5. Add executable-example checks for
the core writes; integration test: non-loopback bind + correct token → 2xx; wrong/no token →
401; bind refusal when auth off. 6. Update SURFACES.md (HTTP API row: guard = those tests).
7. Commit in logical groups (token, bind, docs).

## Verification

Run the daemon: (a) fresh start → token file exists, documented curl with token succeeds;
(b) `--http-bind 0.0.0.0` + token → reachable from a container on native Linux-style
bridge (or the closest testable equivalent on this machine), unauthenticated write → 401;
(c) docs examples executed verbatim pass.

## Definition of done

- [ ] Token: persisted, rotatable, flag/env-presentable, remote-endpoint capable.
- [ ] Bind flag shipped with the auth precondition; container recipe documented and true.
- [ ] Networking & security model page exists; all four entry docs reference it; zero
      401-ing examples remain.
- [ ] Stability markers on /v1/mdns + /v1/certmesh; guards + SURFACES.md updated.
