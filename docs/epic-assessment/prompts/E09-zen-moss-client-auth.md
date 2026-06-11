# E09 — Moss Verifies Clients; the Holes Close

**Repo(s)**: Zen Garden · **Phase**: C · **Prereqs**: E08 (CSR certmesh, new koi version),
E04 (published-dep plumbing) · **One session** · Read [CHARTER.md](CHARTER.md) first.

## Mission

Make the pond's mutual-trust story actually mutual, and close the LAN-exposure holes that
disqualify Zen Garden for its duty-of-care audience. Today moss *serves* TLS from certmesh
certs but **verifies nobody** (`with_no_client_auth`, "mTLS deferred to Phase 4"), while
port 7185 serves unauthenticated reboot/shutdown/offering-delete with pond inactive — and
`POST /api/v1/stone/deploy` (root code-push) is registered in **both** route sets, so it is
unauthenticated **even with pond active**. The same change that fixes the disqualifying
defect ships the differentiating capability (governed fleet operations later ride this).

## Context (verify each)

- `ZEN/src/moss/src/bootstrap/tls.rs:100-101` — `.with_no_client_auth() // Phase 2: server
  TLS only. mTLS deferred to Phase 4.` Cert paths: `{data_dir}/koi/certs/{stone}/`
  (tls.rs:119-134).
- Rake already presents a client identity (`ZEN/src/rake/src/main.rs:124-134`) and installs
  the pond CA into the OS store (`enrollment.rs:118`) — the client half exists.
- The holes: ZG assessment `maturity.md` security row (unauthenticated :7185 surface;
  deploy in both route sets; `changeme` default invite passphrase; NoAuth middleware wired
  to nothing). Verify the route registrations in moss's API setup before changing them.
- First update the koi crate deps to E08's version (CSR protocol) — pond enrollment code in
  `api/v1/pond.rs:454-489` and `domain/security/pond_lifecycle.rs:81-133` must move to CSR
  (keys generated stone-side).

## DECIDED

1. **Client-cert verification on**: moss's TLS config uses a client-cert verifier rooted at
   the pond CA for the HTTPS listener. Role/identity from the presented cert (CN/SAN per
   the roster's conventions) becomes available to handlers.
2. **Route-set hygiene**: `deploy` (and any other mutating verb found in both sets) exists
   ONLY in the authenticated set. With pond active: mutations require a verified client
   cert. With pond inactive: mutating endpoints are **disabled** (clear 403 with a "create a
   pond or use the local console" message) — not open. Read-only status stays available.
3. **`changeme` dies**: no default passphrase; generation or explicit operator input.
4. The mTLS posture is recorded honestly in ZG docs: which endpoints require client certs,
   what pond-inactive mode can and cannot do.
5. **Guards**: integration tests — request with valid pond client cert → 2xx; without →
   401/403 on every mutating route (enumerate them in the test, so a future route added to
   the wrong set fails CI); deploy unreachable unauthenticated in both modes.

## DEFAULT

- Lantern/other in-fleet clients: enroll them as pond members if they mutate; read-only
  paths may stay server-TLS-only this session (note in SURFACES).
- Phasing inside the session: koi-dep bump + CSR adaptation first (compile green), then
  verifier, then route hygiene.

## Plan of record

1. Bump koi crates to E08 version; adapt pond enrollment to CSR (stone-side keygen).
2. Implement the client-cert verifier + identity extraction. 3. Audit every route
registration; move/disable per DECIDED 2; remove `changeme`. 4. Tests per DECIDED 5 (use the
task-supervisor/test conventions ZG already has). 5. Update rake if needed (it mostly
works already). 6. Docs: the pond security model page; update help examples touched.
7. SURFACES.md (pond row: guard = the mTLS/route-hygiene test suite). 8. Commit in groups
(dep bump, mTLS, hygiene).

## Verification

Live two-process check on one machine: pond ceremony → enroll → curl with client cert
succeeds on a mutating route; same curl without cert → 403; deploy without auth → 403 in
both pond modes; grep route tables for any mutating route still in the open set (expect
none).

## Definition of done

- [ ] moss verifies client certs; rake round-trips against it.
- [ ] No mutating endpoint reachable unauthenticated in any mode; deploy fixed; changeme
      gone.
- [ ] Pond enrollment is CSR-based end to end (no key material in transit).
- [ ] Enumerated-route guard test in CI; docs honest; SURFACES.md updated.
