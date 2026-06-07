# SEC-0001: Fleet Identity & Trust Fabric — one verifiable security-context envelope, graded from zero-config to fleet

**Status**: **Accepted (2026-06-06)** — positions ratified by the architect. Founds the **`Koan.Security.Trust`** pillar. **Extends and supersedes DEC-0053** (completes the inbound half it deferred; replaces its deterministic-secret/HS256 dev model with asymmetric signing) — DEC-0053 marked *Superseded by SEC-0001*. Implementation: **Phases 0–2 done** — return-URL fix (§10), asymmetric signing (§11), and the full-collapse identity core (§17 Phase 2: the `Koan.Security.Trust` pillar, ES256 issuer, inbound `Koan.bearer` verifier, ambient `Identity`, zero-config dev identity, fail-closed boot guard; plus the collapse — TestProvider demoted to opt-in, both deterministic-key footguns removed, `Koan.Web.Auth.Services` decoupled from the TestProvider). The authorization-model consolidation (originally §17/2k) was carved out to **SEC-0002**. Phases 3–7 (revocation, DX rungs, fleet issuer/attestors, version-pinned envelope + RFC 8693, federation) remain.
**Date**: 2026-06-06
**Deciders**: Enterprise Architect
**Scope**: Define Koan's inbound token/credential validation and a unified **fleet identity/trust fabric** — one verifiable security-context envelope that travels across HTTP, MCP, Messaging, and Jobs, with a capability-graded bootstrap (zero-config dev → shared key → enrolled fleet), a resource-side authorization seam, and near-real-time revocation. Establishes the DX surface, the trust-anchor ladder, the token contract, the authorization placement rule, and the revocation mechanism.
**Provenance**: Informed by a platform investigation (current auth surface) + two prior-art research workflows (established playbook: SPIFFE/SPIRE, HashiCorp Vault, Kubernetes, Dapr/service-mesh, cloud workload identity; frontier scan: PASETO, Biscuit/macaroons, IETF Transaction Tokens, OpenID CAEP/Shared Signals, Sigstore), each adversarially fact-checked. Corrections from that fact-check are honored inline (see §19).
**Related**: extends/supersedes **DEC-0053** (service-to-service auth) · amends **WEB-0043** (BFF/cookie-first becomes an explicit *posture*, not the only one) · **WEB-0051** (interactive provider election does not apply to non-interactive bearer) · **WEB-0049** (role attribution / claims transformation) · **WEB-0047** (capability authorization) · builds on **ARCH-0084** (capability model + provider election) · **ARCH-0086** (`KoanModule` bootstrap + `Report`) · **ARCH-0079** (integration tests as canon) · **ARCH-0075 / ARCH-0078** (cache topology + coherence channel — reused as the revocation transport) · **JOBS-0005** (ledger-as-truth + distributed tier — reused as durable backing + the cross-hop carrier) · `Koan.Messaging.IMessageBus` · `[KoanDiscoverable]` + `KoanRegistry` (auto-registration) · the Koan redesign initiative ("fewer but more meaningful parts").

---

## 1. Context

This ADR began as a one-line dev bug and escalated to a pillar.

**The bug.** After a successful sign-in, the OAuth/OIDC callback in `AuthController` 500s on an allowlisted **absolute** return URL: `SanitizeReturnUrl` (challenge time) accepts an allowlisted absolute URL into the `Koan.auth.return` cookie, but the callback then calls `LocalRedirect(ru)`, whose `IsLocalUrl` check rejects any URL with a host — so a URL that passed the allowlist is rejected by the redirect. The two checks disagree (§10).

**The step back.** Triaging that surfaced the real gap. A platform investigation established, against the actual code:

- Authorization keys **only on `ClaimTypes.Role`** (`AuthorizationPolicyExtensions` `RequireRole`); the `Koan.permission` claim is informational and has **no** authorization effect.
- There is **no inbound bearer/token validator anywhere** in `src/**` — no `AddJwtBearer`, no `AuthenticationHandler` that validates an incoming `Authorization: Bearer`. The only scheme satisfying `[Authorize]` is the `Koan.cookie` cookie scheme.
- `Koan.Web.Auth.Services` (DEC-0053) implements only the **outbound** half — `ServiceAuthenticationHandler` is a `DelegatingHandler` that *attaches* a token; **nothing validates one inbound.**
- The dev `TestProvider` default token is an **opaque SHA256 hash** (`UseJwtTokens=false`); its JWT path signs with a **deterministic, publicly-derivable HMAC key** (`JwtTokenService` derives the key from `issuer:audience:test-provider-key`), and DEC-0053's dev client secret is `SHA256("koan-dev-secret-…")`. Both are forgeable by anyone who can read the open-source repo.

So Koan claims a microservices/fleet story (DEC-0053, the MCP pillar, the Jobs distributed tier) but **a Koan service cannot verify a token another Koan service sends it.** DEC-0053 built the outbound half and explicitly listed RFC 8693 token exchange and audience-specific tokens as *future work*; this ADR completes the concept.

**The strategic frame (why this passes the redesign bar).** JWT/token handling here is **subtractive, not additive**: one verifiable security-context envelope replaces a scatter of unverified identity headers (`X-Koan-Target-Service`), per-service shared secrets, session lookups, and per-channel identity plumbing. The demand is real and concentrated in **orchestration across machines in a fleet**. Framed as "add JWT auth" it would be scope sprawl; framed as **"complete and unify *service/token identity* into one cross-pillar capability"** it is a consolidation that completes DEC-0053's half-built concept and serves ≥4 channels (HTTP, MCP, Messaging, Jobs) with one primitive. It is therefore in scope for the "fewer but more meaningful parts" initiative.

---

## 2. Forces / principles

1. **Reference = Intent.** Referencing the pillar enables identity; referencing a provider/attestor package trusts that issuer. No manual wiring.
2. **Zero-config floor, one-line rungs.** Dev is *nothing*; each tier up is *one env var / one command*. You never rewrite — you climb.
3. **Fail loud and closed, never silent and open.** The dev auto-identity is the reason most frameworks won't auto-login; Koan can, *because* prod with no auth source **refuses to boot** and the boot report states the active posture. The self-report is the security mechanism.
4. **Dev and prod differ by *config*, not *code*** (the SPIRE/Dapr lesson). Same consumption API at every rung; only the attestor and key-backing swap.
5. **Whoever can verify must not be able to forge.** Asymmetric by default: the verifier holds a public key and cannot mint. (The shared-secret rung is a deliberate, scoped exception — §7.)
6. **One envelope, four channels.** The security context is channel-agnostic — it rides an HTTP header, an MCP request, a bus envelope, or a job record, validated by one core.
7. **Authentication in the token; authorization at the resource.** Coarse identity/roles travel; fine-grained, fast-changing, revocable decisions resolve where the resource lives.
8. **Reuse the broadcast plane we already built.** Revocation rides the existing cache-coherence channel; durable state rides the existing Jobs ledger. The hard part is already shipped.
9. **Hold the line.** This is a *workload/fleet* trust fabric, not a public IdP, not a mandatory mesh, not a mandatory policy engine. Exotic tiers (hardware attestation, offline attenuation) are optional and graded, never the floor.

---

## 3. Vocabulary (decided)

| Term | Meaning |
|---|---|
| **`Koan.Security.Trust`** | the new pillar that owns inbound validation, the in-process issuer, the trust ladder, and the revocation seam. |
| **KSVID** *(Koan Verifiable Identity Document)* | the credential the fabric issues — an asymmetric, `aud`-bound, short-lived signed envelope. Name deliberately echoes SPIFFE SVID so the model can federate outward later. |
| **Identity string** | `koan://<trust-domain>/<app-id>[/<instance>]` — e.g. `koan://acme.fleet/billing-api/7f3a…`. Doubles as a stable `sub` and aligns with Koan's partition/source addressing. |
| **Trust mode / rung** | the auto-detected posture: `dev` (zero-config) · `shared-key` · `fleet` (enrolled) · `federated` (external IdP). |
| **Work envelope** | the immutable, frozen-intent token minted at a trust boundary and forwarded byte-for-byte across hops (TraTs-shaped — §7). |
| **Fleet issuer** | the in-process / HA issuer that mints KSVIDs (the Dapr-Sentry analog). Self-signed by default; one knob to root under existing PKI. |
| **Attestor** | the pluggable mechanism by which a joining node proves itself with no pre-shared secret (k8s SA token / cloud instance-identity doc / TPM / single-use join code). |
| **`IAuthorize`** | the single resource-side decision seam `(subject, action, resource, ctx) → allow/deny` that controllers, the bus, and the jobs dispatcher all call. |
| **Epoch** | a tiny per-subject revocation counter embedded in the KSVID at mint; bumping it invalidates that subject's outstanding tokens (§9). |

---

## 4. Decision — the DX surface (the consumer-facing shape)

The entire surface a developer touches is **four things** — `[Authorize]`, `Identity.Current`, `Identity.Revoke(id)`, and (only at fleet scale) `koan enroll`. Everything else is a package reference and a boot-report line. The trust **mode is auto-detected** from what is configured; the consumption API is identical at every rung.

**Provisional names** (architect to confirm): pillar `Koan.Security.Trust`; ambient `Identity.Current`; credential `KSVID`.

```csharp
// Program.cs — unchanged
builder.Services.AddKoan();
```

```csharp
[Authorize]                      // signed in
[Authorize(Roles = "admin")]     // role required  (keys on ClaimTypes.Role — unchanged from today)

var me = Identity.Current;        // ambient principal, same as the rest of Koan's ambient model
me.Id; me.Roles; me.Is("admin");

Identity.Revoke(userId);          // "log out everywhere" — denied on every node in ~1s
```

### 4.1 The rungs (decided)

| Rung | You add… | Mode | Trust root | For |
|---|---|---|---|---|
| **0 — dev (zero-config)** | nothing | auto dev identity | ephemeral in-process key | someone building a simple app |
| **1 — solo / small trusted net** | `KOAN_AUTH_KEY` in `.env` | shared-secret | the shared key | one operator, flat fleet |
| **2 — fleet** | `Koan:Security:Trust:Issuer` + `koan enroll` | per-node KSVID | fleet issuer | a real fleet / large team |
| **3 — cloud / hybrid** | `Issuer` = external IdP URL | federated | external IdP / cloud WI | enterprise / multi-cloud |

- **Rung 0 is the floor and rides with `Koan.Web`** — a web app is auto-signed-in as a `dev` identity (full access) with **no package, provider, or token**. `[Authorize]` passes; `Identity.Current` is populated. Persona/anonymous testing via a trivial, secret-free switch: `?_as=alice&_roles=reader`, `?_as=anonymous`. You add the `Koan.Security.Trust`/`Koan.Auth` reference only when you want *real* (external/fleet) auth.
- **Rung 1** is the architect-proposed shared `.env` secret — **accepted as sufficient** for flat, small, single-operator deployments (§7). It is strictly better than DEC-0053's publicly-derivable dev secret.
- **Rung 2/3** are the capability ladder of §5.

### 4.2 The safety invariant (decided — this is what makes zero-config acceptable)

The dev auto-identity is **`KoanEnv.IsDevelopment`-only** and **structurally absent** from the DI/auth graph in any other environment — not "registered but denied," *absent*. Running outside Development with **no** configured auth source is a **fail-closed boot error**, not a silent default:

```
[Koan] Auth: DEV — you are 'dev' (full access). Not active outside Development.
...
[Koan] FATAL: Production with no auth source.
       Set Koan:Security:Trust:Key (shared secret) or :Issuer (IdP), or explicitly opt into anonymous.
       Refusing to start.
```

The boot report states the active rung on every startup (`DEV` / `SHARED-KEY` / `FLEET koan://… attestor=…` / `FEDERATED issuer=…`). Honesty over false "everything just works."

---

## 5. Decision — bootstrap / trust-anchor capability ladder

**The invariant (borrowed from SPIFFE/SPIRE, Dapr Sentry, Tailscale, Nebula):** a node **generates its own keypair locally — the private key never leaves it** — and presents **evidence a trusted third party already vouches for**; the issuer validates that evidence and returns a short-lived signed KSVID. The "bottom turtle" is pushed down to a trust anchor that **already exists in the environment** (the cloud provider's signing key, the k8s API server, a TPM EK chain), never a secret shipped with the node.

Each rung swaps only the **attestor** and the **trust root**, never the consumption API — the same dev/prod-by-config symmetry SPIRE and the service meshes prove out.

- **Tier 0 — dev.** In-process issuer, **ephemeral asymmetric (ES256/EdDSA) key generated per-process in memory**, ~1h KSVIDs to loopback/in-process workloads, JWKS published locally for offline verification, wiped on restart. No token typed.
- **Tier 1 — single host.** Persistent local issuer; root key **sealed at rest** (DPAPI/keychain/ACL'd file). **TPM sealing is strictly opt-in and degradable** — never a non-degradable default (the Tailscale Jan-2026 TPM-default reversal is the cautionary case). Local processes attest via **OS selectors** (uid/gid/path/container labels) over a local socket — the workload presents *nothing*; the issuer introspects the caller.
- **Tier 2 — self-contained fleet.** A **fleet issuer** (HA-capable, the Dapr-Sentry analog) holding a CA — self-signed on first boot by default, one knob to root under existing PKI (the Istio plug-in-CA / Consul-Vault pattern). A new machine earns its first credential with **zero pre-shared secret**, by an **auto-detected** attestor:
  - **Kubernetes:** projected ServiceAccount token → validated via `TokenReview`.
  - **Cloud VM:** instance-identity document (AWS IID / GCP metadata JWT / Azure MSI) → provider signature verified out-of-band.
  - **Bare metal w/ hardware:** TPM DevID (proof-of-possession + proof-of-residency) or `x509pop`.
  - **Greenfield bare metal, no anchor (the honest weak rung):** a **single-use, short-TTL join code** — `koan enroll --code <X>`. The node generates its keypair and sends only the CSR + code; the issuer signs and returns the KSVID. **A second use fails and surfaces as an alert** (Vault's TOFU lesson), and **the boot report states `attestor=join-code`** so the weak rung is never silent.
- **Tier 3 — cloud / hybrid.** Publish **OIDC discovery** (`/.well-known/openid-configuration` + JWKS) so KSVIDs exchange for cloud creds with no Koan-specific key plumbing; inbound, **federate** to an external IdP / cloud workload identity via standard OIDC validation (the near-zero-config Spring resource-server move: one `issuer-uri`, discovery deferred to first use so startup isn't coupled to IdP uptime). Trust scope (`sub`/`aud` conditions) is **first-class validated config**, never a free-text wildcard (the GitHub-Actions-OIDC confused-deputy footgun).

---

## 6. Decision — token structure & lifecycle

### 6.1 Format and the no-negotiable-alg discipline (stolen from PASETO)

The internal credential is an asymmetric KSVID, **ES256 default** (EdDSA/RS256 allowed), `kid` in header for rotation. The verifier is **secure-by-default**; insecure knobs require loud opt-in.

**Decision — pin the ciphersuite to a version constant carried *outside* the negotiable payload; validate the expected suite *before* any crypto runs; express crypto agility as bumping the version (a capability bump), never as an in-band header field.** This is the PASETO principle, and it **structurally** removes the JWT *header-negotiation* attack class — `alg=none`, RS256→HS256 confusion, and `kid`/`jku` injection are unrepresentable. (Honoring the fact-check: this kills the *header-negotiation* class; it does **not** by itself eliminate all key-type confusion — that still needs the key-type assertion + test-vector discipline.)

**Decision — steal the *principle* for internal tokens; keep JWT/OIDC at the external edge.** The .NET PASETO library (`paseto-dotnet`) is community-maintained, so adopting PASETO as the *external* default would trade enormous OAuth/OIDC interop for a smaller blast radius nobody at the edge asked for. Internal S2S / job / coherence tokens (where Koan owns both ends) use the version-pinned format; the **edge inbound validator speaks standard JOSE/JWT** for IdP interop.

### 6.2 Claim contract (coarse only — see §8)

| Claim | Purpose | Rule |
|---|---|---|
| `iss` | issuer / trust domain | validated against the pinned issuer |
| `sub` | the `koan://td/app-id` identity | stable for policy + audit |
| `aud` | target transport/service | **REQUIRED + validated; reject missing/mismatch** — closes confused-deputy |
| `exp`/`iat`/`nbf` | lifetime | reject expired / not-yet-valid |
| `typ` | `koan-ksvid+jwt` (or version-pinned) | explicit, mutually-exclusive per kind — a bus token can't replay as an HTTP token |
| `roles` | **coarse** broad roles | maps to today's `ClaimTypes.Role` |
| `epoch` | per-subject revocation epoch | the one stateful-ish claim (§9) |
| `act` | on-behalf-of delegation chain (RFC 8693) | top-level = current actor; **nested = audit-only, MUST NOT drive authz** |
| `cnf` | sender-constraint binding | `x5t#S256` (mTLS, RFC 8705) or `jkt` (DPoP, RFC 9449) |

### 6.3 Lifetimes, rotation, sender-constraint, delegation (decided)

- **Lifetimes:** ~**15 min** edge (HTTP/MCP); ~**1h with auto-rotation at 50% TTL** east-west. Long-lived authority lives in the **attestation/re-mint loop**, never a stored bearer. The **framework owns rotation** (app sees no token, writes no renewal code); add **jitter** and refresh *well before* expiry (a half-working renewal loop is worse than none — the Vault-Agent lesson). Honoring the fact-check: short-TTL **complements**, not replaces, revocation — it adds a hard dependency on a reachable re-issuer, so §9 is required, not optional.
- **Sender-constrained by default for the fleet; bearer is the explicit downgrade.** One `cnf`-validation core, two adapters: **mTLS-bound (`x5t#S256`)** east-west (free where the transport already does mutual TLS — and note this is *token binding*, **not** a mandatory mesh; DEC-0053's rejection of mandatory mesh stands), **DPoP (`jkt`)** at the edge (no PKI). **Honor the DPoP caveat:** ship the server-nonce path for sensitive scopes and a `jti` replay cache; DPoP does not stop same-endpoint replay without a nonce, nor XSS, nor protect the body.
- **On-behalf-of as a framework primitive (RFC 8693 token exchange — DEC-0053's listed future, now decided).** When a job/message/HTTP call crosses a hop, the runtime **re-mints a correctly-audienced, scope-narrowed** token carrying the `act` chain rather than forwarding the inbound token. **Default to delegation (keep actor identity) over impersonation** so audit survives; enforce `may_act` at issuance.
- **Key rotation designed in from day one** (`kid` + JWKS, overlapping old/new) — it is Kubernetes' worst pain point and caused Linkerd's intermediate-expiry outage; short-lived intermediates + auto-renew or loud early warnings.

---

## 7. Decision — the immutable work envelope (cross-hop orchestration)

This is the primitive the fleet needs and the reason the whole fabric is "great" rather than merely correct. **Decision — at every trust boundary (`Job.Submit()`, HTTP/MCP ingress) mint *one* immutable envelope** (TraTs-shaped — IETF OAuth WG `draft-ietf-oauth-transaction-tokens`, in WG Last Call) that freezes:

- `sub` (who) + the **original computed intent** (`purp`/`tctx`) — the *narrow* purpose of *this* unit of work, not coarse external scope,
- a single `txn` correlation id that **is** Koan's existing `(WorkType, WorkId)` ordering/correlation key,

and is **forwarded byte-for-byte** across HTTP, MCP, `IMessageBus`, and the Jobs ledger. Downstream nodes authorize against the submitter's **original intent**, never their own ambient context — which kills mid-chain privilege escalation. The **only mutable field is an append-only `req_wl` provenance trail**.

**Steal the shape, not the topology.** The TraTs spec mandates a single logical Transaction Token Service per domain — a centralization/SPOF that contradicts Koan's ledger-as-distributed-truth. Decision: take the envelope shape and reuse claim names (`sub`, `aud`, `txn`, `purp`, `tctx`) for legibility; let **per-node minting + the Jobs ledger** replace the central TTS.

The trio that composes the fabric: **freeze intent (TraTs) + make it unforgeable (§6.1 version-pinning) + take it back over the plane we already run (§9 CAEP-over-coherence).** No off-the-shelf product gives this whole; Koan can, cheaply, because the broadcast plane already exists.

---

## 8. Decision — where authorization lives

**Rule (the verified consensus — NIST SP 800-162/800-207, OASIS XACML, OpenID AuthZEN 1.0):** **coarse identity + broad roles in the token; fine-grained, fast-changing, revocable authorization resolved at the resource** through one `IAuthorize(subject, action, resource, ctx)` seam that `EntityController<T>`, the bus, and the jobs dispatcher all call. Resolving at the resource is what keeps revocation instant *by construction*.

Honoring the fact-check: the boundary is token **lifetime and breadth**, not "never permissions in a token." RFC 9396 (Rich Authorization Requests) legitimately embeds fine-grained grants in *short-lived, consent-scoped* tokens. So the precise rule is **"no fine-grained/revocable authz in *long-lived, broadly-scoped* tokens."**

**Maps onto Koan today for free:** authorization already keys only on `ClaimTypes.Role`, and a KSVID carries `roles`. **Tier 0 in-process RBAC/ownership** (read broad roles straight from the token, **zero external dependency**) is the floor — enough for the 80% CRUD case. Formalize it as the `IAuthorize` floor so nobody trusts fine-grained grants from the token.

**Authz capability ladder (each rung = a reference, mirroring the cache/jobs pillars):**
- **Tier 0 — in-process RBAC/ownership** (no deps). The default.
- **Tier 1 — ABAC PDP adapter.** Lead with **Cerbos** (readable YAML, opinionated, stateless) for DX, **Cedar** where formal-analyzability / AWS matters. **Do not expose Rego as the primary surface** — its learning curve is the antithesis of "simple for teams."
- **Tier 2 — ReBAC adapter** (sharing/hierarchy / "who-can-see-X"): **OpenFGA** (DX) or **SpiceDB** (full Zanzibar incl. consistency tokens). Surface staleness/zookie semantics explicitly.

**An external PDP is opt-in, never mandatory** (a network PDP on every request is a SPOF + tail-latency amplifier and violates "everything just works"). **Default fail-closed** (deny on PDP error); **decision-cache with explicit invalidation riding the existing coherence channel.** Declarative, identity-keyed policy (`trustDomain/app-id`, `defaultAction: deny`) fits the `[JobGate]`-style attribute ethos (Dapr ACL pattern).

---

## 9. Decision — revocation

**Default: revocation-by-expiry (short TTL + rotation).** 15 min edge / 1h auto-rotated fleet means ~99% of apps need **no** deny-list, introspection, or extra moving parts. Honoring the fact-check, this **complements** rather than replaces revocation (it adds a re-issuer dependency and leaves a TTL-sized window), so the strong option ships alongside it.

**Strong guarantee — CAEP-shaped, over infrastructure we already own (reuse, do not rebuild):**

1. **Primary — per-subject `epoch`.** Each principal carries a tiny `epoch`; the KSVID embeds the subject's epoch at mint. On a security event ("log out everywhere," role change, compromise), **bump `epoch`**. Verifiers reject any KSVID whose embedded `epoch` < the subject's current epoch.
   - **Mechanism:** the bump publishes a subject-keyed event (the CAEP vocabulary — `session-revoked` / `token-claims-change`, keyed by a complex `sub_id`) as an `Evict` over the **existing `ICacheCoherenceChannel`** (origin-filtered, echo-suppressed — the same fan-out the cache pillar already uses) → every node updates a tiny in-memory `subject→epoch` map → the next request for that subject is denied. **Reuse `CoherenceCoordinator`, not a parallel mechanism.**
   - **Properties:** O(#subjects) state (one row each), not O(#tokens); sub-second in-cluster; eventual cross-region; durably backed by the **Jobs distributed ledger**, which replays missed events on reconnect (CAEP's stream-resync, for free).
2. **Secondary — per-`jti` deny-list** for "kill *this* session/token now," with **TTL = the token's remaining life** so it self-evicts and never grows unbounded.

**Steal the CAEP *vocabulary*, not the *transport*.** Do **not** implement the SSF Stream Management API / push-poll negotiation / cross-org transmitter-receiver trust — that is enterprise-IdP machinery and the elegance trap. Koan's coherence channel **is** the transport.

**Why not OCSP/CRL — with the fact-check's precision:** we reject **per-request synchronous status checks (OCSP-shaped)** and **unbounded, slowly-synced lists (classic CRL)** — *not* "revocation lists in general." Our `epoch` + pub/sub model **is** a bounded, push-propagated revocation list — precisely the surviving pattern (Let's Encrypt deprecated OCSP but moved *to* CRLs, for privacy). We get the list's correctness with no round-trip and no staleness. **Token introspection (RFC 7662)** is offered only as opt-in for explicitly opaque/reference-token scopes.

---

## 10. Decision — the original return-URL bug (tactical, ship regardless)

Captured for completeness; independent of the fabric and should be fixed immediately.

- **The allowlist is the security boundary and *is* meant to permit absolute/cross-origin URLs** (WEB-0043 documents allowlisted absolute prefixes; `SanitizeReturnUrl` already accepts them). Therefore **fix A**: in the callback, re-check the stored return URL against the allowlist and use **`Redirect(returnUrl)` for an allowlisted-absolute URL, `LocalRedirect(returnUrl)` for a relative path.** Option C (reject absolute) contradicts canon and is rejected.
- **Fix the `Logout` twin** (same `LocalRedirect(SanitizeReturnUrl(...))` latent bug) in the same change.
- **Extract a shared `RedirectToReturnUrl(ru, allowList)` helper** so the challenge-time and callback-time checks can never drift apart again.

---

## 11. Decision — security posture & footgun removal

- **Kill the deterministic-key footgun.** Remove `JwtTokenService.GetSigningKey`'s symmetric-HS256-from-public-formula path and DEC-0053's `SHA256("koan-dev-secret-…")` client secret. **Switch to asymmetric signing (ES256/EdDSA) with a per-process, non-deterministic key**; a public verifier can never become a signer (this also closes the RS256→HS256 class by having no symmetric secret at all). Refuse to start outside Development if a deterministic/symmetric dev key is in play.
- **Fail-closed gating** (§4.2): the dev auto-identity and any dev validator are **absent from the prod DI graph**, env-gated like `TokenController.cs:20` / `TestProviderContributor` / the `ProviderRegistry` production freeze — and that gate stays **decoupled** from `AllowMagicInProduction`.
- **JOSE hygiene at the edge** (RFC 8725): asymmetric-only, verifier-pinned alg allow-list, sanitized `kid`, whitelisted `jku`/`x5u`, explicit `typ`, mandatory `aud`/`iss`/`exp` validation.

---

## 12. What's new vs reused (redesign accounting) + anti-goals

**New parts:** the **inbound KSVID/JWT validator** (DEC-0053's missing half); the **in-process / fleet issuer** (Dapr-Sentry analog); **node-attestation plugins** (k8s-SA / cloud-IID / TPM / join-code); the **version-pinned envelope + `cnf` sender-constraint core**; the **RFC 8693 exchange primitive**; the **`IAuthorize` seam**.

**Reused (the leverage — the hard parts are already built and proven):** `IMessageBus` + `ICacheCoherenceChannel` / `CoherenceCoordinator` (revocation fan-out); the **Jobs distributed ledger** (durable epoch/deny backing + the cross-hop carrier); the **capability model** (attestor/PDP/sender-constraint rungs are self-detected capabilities); **self-reporting boot** (every node states how it attested + its trust domain); `KoanAutoRegistrar` / `KoanModule` (Reference = Intent enrollment); the existing **cookie BFF** (`Koan.cookie`) and DEC-0053's outbound `DelegatingHandler` (which now gains its inbound counterpart and an asymmetric key).

**Anti-goals (decided):**
1. **Not a public IdP / OAuth authorization server for end users** — this is a *workload/fleet* fabric; human SSO stays in the cookie BFF + external IdP. The fleet issuer issues only *internal, short-lived service identities*; **federation is outbound** (OIDC discovery), not "run an OAuth server for the world."
2. **Not a mandatory mesh / sidecar** — the issuer is in-process for dev/single-host; externalize only at fleet scale. (mTLS appears only as optional *token binding*, never required mesh infra.)
3. **Not a mandatory external PDP** — in-process RBAC is the floor.
4. **No divergent dev/prod code path** — dev and prod differ by attestor + key-backing (config/reference), never by code.
5. **No OCSP-shaped per-request status calls and no unbounded CRL** — revocation is push-propagated and self-bounding.
6. **No registration-entry authoring tax** — infer selectors from entity/module metadata (Reference = Intent); hand-authoring is power-user only (avoid SPIRE's registration-entry burden).

---

## 13. On the shelf (graded, optional, added only when real)

Explicitly **deferred**, not part of the floor — each is a future capability rung:

- **Mid tier (shared key *with* per-node revocation, no full enrollment)** — **deferred; not needed now.** Revisit if a flat-fleet operator wants to revoke one node without standing up an issuer.
- **Biscuit / macaroon offline attenuation** — for cross-node least-authority delegation (a child job gets a token narrowed to exactly `(WorkType, WorkId, op)`). Steal the chained-signature *attenuation-only invariant*; **skip the embedded Datalog engine** (a second policy language = cognitive-load trap).
- **Sigstore-style short-lived + issuance-ledger audit** — steal the audit consequence (`Entity<IdentityGrant>` appended to the existing ledger CAS/outbox, answering "which node held what authority, when, under whose identity"); skip the Merkle/transparency-log cryptography. (Fact-check note: real Sigstore logs issuance to a **CT log**, not Rekor — irrelevant here; the "log" is just an append-only ledger Koan already has.)
- **TPM / confidential-compute attestation (SEV-SNP/TDX/Nitro, Keylime)** — an **optional Tier-4 high-assurance** rung; **never the floor** (it breaks the dev laptop / CI / non-TPM VM).
- **Filter-cascade / accumulator revocation (CRLite-style)** — premature; per-subject `epoch` eviction is simpler to reason about. Graduate only if per-subject events stop scaling.

---

## 14. Relationship to existing ADRs

- **DEC-0053 (service-to-service auth)** — **extended and superseded.** SEC-0001 completes the inbound half DEC-0053 deferred, replaces its deterministic-secret/HS256 dev model with asymmetric signing, generalizes service-to-service auth into a cross-channel fabric, and decides DEC-0053's listed futures (RFC 8693 token exchange, audience-specific tokens). DEC-0053's rejection of *mandatory mesh* stands; its rejection of *mTLS wholesale* is narrowed to "mTLS as optional token-binding." Mark DEC-0053 *Superseded by SEC-0001* on acceptance.
- **WEB-0043 (multi-protocol auth, BFF/cookie-first)** — **amended.** BFF/cookie remains the default for human users but becomes an **explicitly declared posture**, not the only one; resource-server (bearer) is a sibling posture. An app declares which it is (or which endpoints are which); the framework does **not** make XSS-vulnerable token-in-browser SPAs accidentally easy.
- **WEB-0051 (provider discovery & election)** — interactive election governs *human* login; **bearer is non-interactive and opts out** of the redirect/election path via explicit `[Authorize(AuthenticationSchemes="…")]`.
- **WEB-0049 / WEB-0047** — the existing role-attribution / capability-authorization machinery is the `IAuthorize` floor; a KSVID principal carrying `ClaimTypes.Role` satisfies it unchanged.
- **ARCH-0079** — every new scheme/seam ships ≥1 integration spec through real `AddKoan()` via `KoanIntegrationHost` (§17).

---

## 15. Consequences

**Positive**
- Closes the inbound auth gap that blocks real fleet/microservice work; a Koan service can finally verify another's token.
- One envelope across HTTP/MCP/Messaging/Jobs — a *consolidation* that nets negative conceptual surface.
- Zero-config dev that is safe by construction (fail-closed in prod); each tier up is one line.
- Revocation and durable backing reuse already-shipped, already-proven machinery (coherence channel + jobs ledger) — the "great" part is nearly free.
- Removes a live forgery footgun (deterministic dev key).

**Negative / costs**
- A second authentication scheme + an issuer + attestors is real new surface to own and keep prod-safe.
- "Proper" lives in operational security (key rotation, multi-issuer, JOSE hygiene) — the code is the easy 20%; a half-built version would be a framework-wide CVE surface. This ADR is a commitment to *own* that, or to stop at the gated dev/shared-key rungs.
- Short-TTL introduces a hard dependency on a reachable re-issuer (mitigated: jittered early refresh, offline JWKS verification, HA issuer).

**Risks (and mitigations)**
- **Mis-gating → prod auth bypass** (deterministic-key forgery / dev-identity leak). → env-absent-from-DI + a dedicated **prod-absence integration test** (§17); gate decoupled from `AllowMagicInProduction`.
- **Bare-`[Authorize]` returns 302 instead of 401 for token callers.** → bearer endpoints use explicit `AuthenticationSchemes="Bearer"`; tested.
- **Key distribution as a new SPOF.** → rotation + short-lived intermediates from day one; HA issuer; verification stays offline via published JWKS so an issuer outage breaks only *re-issuance*, not verification.
- **Clock skew across the fleet.** → tight bounded skew (~60 s edge), surfaced in the boot report.
- **Sender-constraint over-promise.** → document DPoP's nonce requirement and that mTLS binding breaks if a gateway terminates TLS without forwarding the thumbprint.

---

## 16. Alternatives considered

1. **Dev-only "accept the test token" affordance, nothing more.** *Rejected as the ceiling* (kept as the floor) — it would not close the fleet gap; the strategic demand is real.
2. **Shared-secret (`.env`) for everything.** *Accepted only as Rung 1* — its fatal property is "whoever can verify can also forge": no per-node identity, no scoping, no single-node revocation, no audit, and one leak = the whole fleet. Fine for flat/small/single-operator; insufficient for a graded fleet. The keypair fixes all four at ~zero call-site cost.
3. **Full SPIRE / service mesh (Istio/Linkerd) wholesale.** *Rejected as the default* — operational weight (HA SPIRE cluster + per-node agent + datastore; sidecars/CNI) violates "library-first, everything just works." We borrow the *model* (two-phase attestation, short-lived auto-rotated credentials, dev/prod-by-config) and embed it; hardware/mesh tiers stay optional.
4. **PASETO as the external default token.** *Rejected* — trades OAuth/OIDC interop for a smaller blast radius at the edge where Koan doesn't own both ends; `paseto-dotnet` is community-maintained. We steal the *version-pinning principle* for internal tokens and keep JOSE/JWT at the edge.
5. **Stay cookie-only (do nothing).** *Rejected* — leaves DEC-0053 a half-built concept and blocks fleet/MCP/mobile.
6. **Mandatory external PDP (OPA-everywhere).** *Rejected* — per-request network SPOF + tail latency; PDP/ReBAC are opt-in rungs over an in-process RBAC floor.

---

## 17. Phased implementation plan (effort-sized; ship-first slice de-risks the rest)

| # | Phase | Size | Notes |
|---|---|---|---|
| 0 | **Tactical return-URL fix** (§10) + shared helper + Logout twin | **S** | Independent; ship now. |
| 1 | **Kill the deterministic-key footgun** → asymmetric signing (ES256), per-process key | **S–M** | Also the dev-token fix; precondition for everything. |
| 2 | **Inbound validator at the edge** (standard JOSE/JWT) + **`IAuthorize` seam** wired into `EntityController<T>`/bus/jobs | **M** | Closes DEC-0053's gap; authz parity is near-free (keys on `ClaimTypes.Role`). |
| 3 | **Epoch-over-coherence revocation** (`Identity.Revoke`, `subject→epoch` map, `ICacheCoherenceChannel` fan-out, ledger backing) | **M** | Proves the "great" part cheaply on existing infra. |
| 4 | **DX rungs**: zero-config dev identity (+ fail-closed prod boot guard, boot-report posture) and **Rung-1 shared key** | **M** | The consumer surface of §4. |
| 5 | **Fleet issuer + attestors** (k8s-SA / cloud-IID / join-code) + `koan enroll` | **L** | Rung 2. |
| 6 | **Version-pinned internal envelope + `cnf` sender-constraint + RFC 8693 exchange + TraTs work envelope** | **L** | The cross-hop fabric (§6.1, §7). |
| 7 | **Federation** (OIDC discovery, external-IdP validation) | **M** | Rung 3. |
| — | **Shelf tiers** (Biscuit attenuation, Sigstore audit ledger, TPM attestation, PDP/ReBAC adapters) | **graded** | Reference = Intent, added when real (§13). |

**Test surface (ARCH-0079 — mandatory):** ≥1 integration spec per seam through real `AddKoan()` / `KoanIntegrationHost`: (1) bearer accepts a valid KSVID; (2) rejects invalid/expired/tampered with **401**; (3) role policy honored across schemes; (4) cookie path unaffected (bare `[Authorize]` still 302/JSON-401); (5) **prod fail-closed** (dev identity/validator absent, app refuses to boot without an auth source) — the security-critical test; (6) revocation: an `epoch` bump denies the subject on a second node within the SLA; (7) cross-hop: a job submitted on node A carries a delegated, re-audienced envelope verified on node B.

---

## 18. Open questions / deferred decisions

1. **Naming** — confirm `Koan.Security.Trust` (pillar), `KSVID` (credential), `Identity.Current` / `Identity.Revoke` (ambient surface), and the `koan://td/app-id` identity string.
2. **Default dev identity roles** — "full access" (frictionless "bam") vs a normal user (forces role testing). Current decision: **full access**, with `?_as=`/`_roles=` to downgrade; revisit if it masks authz bugs.
3. **Internal PASETO library adoption** — steal-the-principle only, or also adopt `paseto-dotnet` for internal tokens? (Steal-the-principle is the safe default.)
4. **Issuer HA / storage** — in-process only at Tier 2, or a standalone issuer process for larger fleets? Backing store for the issuer CA + epoch table (the Jobs ledger is the candidate).
5. **Mid tier** — deferred (§13); define the trigger that would pull it forward.
6. **Cross-region revocation SLA** — acceptable epoch-propagation latency cross-region (in-cluster is sub-second; cross-region is eventual).

---

## 19. References

**Prior art borrowed (with fact-check corrections honored):**
- SPIFFE/SPIRE — workload identity, two-phase node+workload attestation, short-lived auto-rotated SVIDs. https://spiffe.io/docs/latest/spiffe-about/spiffe-concepts/
- Dapr — Sentry CA, automatic mTLS with SPIFFE identities (default on k8s, opt-in self-hosted). https://docs.dapr.io/concepts/security-concept/
- HashiCorp Vault — AppRole / response-wrapping / cloud auth, secret-zero, dev-mode ephemerality. https://developer.hashicorp.com/vault/docs/agent-and-proxy/agent
- Kubernetes — projected ServiceAccount tokens (TokenRequest API: audience-scoped, short-TTL, auto-rotated); kubelet TLS bootstrap. https://kubernetes.io/docs/concepts/security/service-accounts/
- Tailscale / Nebula — node-generates-own-key invariant, pre-auth/join keys, the TPM-default reversal cautionary case.
- **PASETO** — version-pinned ciphersuite, validate-before-crypto, implicit assertions. *Correction honored:* removes the JWT **header-negotiation** attack class, not all crypto attacks. https://github.com/paseto-standard/paseto-spec
- **IETF Transaction Tokens** — frozen-intent envelope across internal hops. *Correction honored:* `draft-ietf-oauth-transaction-tokens` is an **OAuth WG** document (in WG Last Call), **not WIMSE** (WIMSE is the complementary workload-identity effort). https://www.ietf.org/archive/id/draft-ietf-oauth-transaction-tokens-08.html
- **OpenID Shared Signals Framework + CAEP** — push-based, subject-keyed security events; *steal the event vocabulary, not the Stream Management API.* SSF/CAEP reached Final 1.0 (2025). https://openid.net/specs/openid-caep-1_0-final.html
- **Sigstore (Fulcio/Rekor)** — short-lived OIDC-bound certs + audit log. *Correction honored:* certificate issuance is logged to a **CT log**, not Rekor.

**Standards:** RFC 8725 (JWT BCP) · RFC 9700 (OAuth Security BCP) · RFC 8693 (Token Exchange) · RFC 9449 (DPoP) · RFC 8705 (mTLS-bound tokens) · RFC 9396 (Rich Authorization Requests) · RFC 7662 (Token Introspection) · RFC 6749/6750/7519 (OAuth/Bearer/JWT, via DEC-0053).

**Koan canon:** DEC-0053 · WEB-0043 · WEB-0047 · WEB-0049 · WEB-0051 · ARCH-0075 / ARCH-0078 (cache topology + coherence) · ARCH-0079 (integration tests as canon) · ARCH-0084 (capability model) · ARCH-0086 (KoanModule) · JOBS-0005 (ledger + distributed tier).

**Investigation provenance:** platform investigation + two prior-art research workflows (this branch, 2026-06-06); load-bearing claims adversarially fact-checked — see the design discussion that produced this ADR.
