# SEC-0005: Governed agent access — grants, audit, the Door

**Status**: **Accepted (2026-06-19)** — the realization of **ARCH-0092 Phase 4 / P3.1** ("governed, revocable, audited agent access"), re-derived onto the [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) gate·constrain·project model (the original P3.1 card predates SEC-0004 and is stale where it conflicts; see *Rejected*).
**Date**: 2026-06-19
**Deciders**: Enterprise Architect
**Scope**: How an agent's access is *granted* (beyond its token), *audited*, and *disclosed* — the governed-access layer over the SEC-0004 gate. Cross-surface (REST + MCP) because the gate is.
**Related**: [SEC-0004](SEC-0004-capability-authorization-gate-constrain-project.md) (the gate this binds to) · [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md) §D / P3.1 · [SEC-0001](SEC-0001-fleet-identity-and-trust-fabric.md) (`Identity.Current`, subject id) · the agent-native **Wall / Door / Verb** model (docs/assessment/09 §8).

---

## Context

SEC-0004 made the gate compute, per request, **`Needs ≤ grant`**: the `[Access]` declaration is the *Needs*, the caller's `ClaimsPrincipal` claims are the *grant*, and the projection advertises exactly what the caller may invoke. Three things are still missing for *governed agent access* — and the 09 §8 review confirms only two are genuinely net-new primitives; everything else **binds to the existing gate**:

1. **Grants beyond the token.** An agent's authority should be issuable server-side — *"kitchen-agent may mutate PantryItem for the next 8 hours"* — **queryable, revocable, expiring**, distinct from whatever scopes its token carries. The token is what the agent *is*; a grant is what it has been *lent*.
2. **Audit.** A mutating agent call against a sensitive entity should leave a queryable trail — through the normal entity path, not a bespoke log.
3. **The Door.** The projection is currently binary (Verb visible / Wall absent). The 09 §8 model adds a third state — a **Door**: a walled verb whose `Needs` exceeds the grant but is *signposted* ("you could do X with grant Y"), drift-proof because the signpost derives from the **same `Needs` that enforces it** (Invariant: *Description = Enforcement*). Default stays **Wall**; admin tiers are **always Walls**.

## Rejected (the stale P3.1 card)

The P3.1 card (docs/assessment/prompts/07/P3.1-governed-agent-access.md) was written before SEC-0004. Its core "DECIDED" points are **rejected**:

- **`[McpEntity(Expose = McpAccess.Read | Mutate)]`** — read-only-by-default + access config back *on* `[McpEntity]`. ARCH-0092 Phase 3.3b **removed** access config from `[McpEntity]` (it is pure exposure; access is the `[Access]` gate). Re-adding it re-forks the per-surface authz the gate just unified. The gate stays the single access authority.
- **`McpAccess.Read | Mutate`** grain — predates the gate's `read`/`write`/`remove` actions.
- **`S16.PantryPal` dogfood** — the sample was retired.

What survives: grants are entities; audit is an entity; revocation rides the cache pillar; agent identity is the threaded principal. Those are kept.

## Decision

Three additions, each binding to the SEC-0004 gate. Nothing per-transport — the gate is the one choke point REST and MCP share.

### 1. `AgentGrant` — server-side grants on the gate

`AgentGrant : Entity<AgentGrant>` (the Koan move — grants are queryable/revocable/observable like any entity):

```csharp
public sealed class AgentGrant : Entity<AgentGrant>
{
    public string Subject { get; set; } = "";     // the agent's subject id (sub / NameIdentifier)
    public string Capability { get; set; } = "";   // an [Access] term: "is:admin" / "has:scope:orders:fulfill"
    public string Resource { get; set; } = "*";    // entity name, or "*" for any
    public DateTimeOffset? ExpiresAt { get; set; }  // null = no expiry
}
```

**Materialization (the integration):** the `EntityFloorAuthorizationProvider` evaluates the gate against the token principal first. Only when the gate **denies** (the common path is unaffected — an Allow/open action never loads grants) does it load the subject's *active* grants for that resource, **materialize them as scoped effective-claims** (a `Capability` of `has:scope:x` → a `scope` claim; `is:admin` → a role claim), and re-evaluate the **same** gate against the enriched principal. So a grant composes with the gate's bags, with `origin`, and with `Constrain` for free — the row-narrowing `Constrain` still applies (a grant satisfies the coarse *Needs*, never the row filter). An anonymous caller has no subject id → no grants (you grant to a known agent).

**Revocation:** grants are loaded fresh per request (memoized per request, not across) — `Remove()` or expiry takes effect on the next call, fleet-wide, with **zero new machinery** (no epoch system; the cache pillar's invalidation is a later perf optimization, not a correctness dependency).

### 2. `AgentAction` — mutation audit

`AgentAction : Entity<AgentAction> { Subject, Resource, Action, EntityId, At }`. An entity opts in with **`[Audit]`**; every **mutating** call (write/remove) to an audited entity writes one `AgentAction` row through the normal entity path (queryable/streamable like everything else). **Reads are never audited** (volume). Written after the gate allows and the mutation succeeds.

### 3. The Door — disclosed walls

An entity opts into disclosure with **`[Door]`**. For that entity, a verb the caller may **not** invoke (the gate denies) is projected as a **door** — listed with `door: { needs: "<the unsatisfied [Access] terms>" }` (named + how-to-unlock) — instead of vanishing. Without `[Door]` the verb is a **Wall** (absent — the default, so admin/privileged tiers are silent by construction: *never default a capability to Door*). The signpost's `needs` is rendered from the same `AccessGate` the gate enforces, so it cannot drift from enforcement. The door is **disclosure only** — the verb is still denied on call.

## Slices

1. **AgentGrant core** — the entity + floor materialization (token-denied slow path) + per-request fresh revocation. Integration tests through `AddKoan()`.
2. **Audit** — `AgentAction` + `[Audit]` + write-once-per-mutation on the endpoint write/delete paths.
3. **Door** — `[Door]` + the projection signpost state in `tools/list` + the `koan://entities` catalog.

## Consequences

- **Backward-compatible:** no `AgentGrant`/`[Audit]`/`[Door]` declared = byte-identical to SEC-0004 today (grants load only on a denial, audit/door are opt-in).
- **One authority:** grants enrich the *gate* decision, so REST and MCP grant identically — no per-transport grant path.
- **Deferred:** per-verb (vs per-entity) Door granularity; cache-backed grant lookup (perf); `owner` as a grantable capability (ownership is row-bound `Constrain`, not a claim — a grant expresses `is`/`has` terms only).

## Amendment (2026-10-30) — each approval is a grant that says when it was issued and to which client

A consumer showing a person "the agents acting as you", each with when it was approved and a revoke, found these gaps.
`AgentGrant` recorded no time of issue, so the consumer read it from the id's UUIDv7 timestamp, which relies on Koan's
default id generation and is not a contract. The client a grant was issued to lived only inside the authorization
server's internal capability term (`mcp:grant:{client}:{scope-hash}`, SEC-0006 D9), so naming the client meant parsing
an internal format. And the server reused a live grant keyed by (subject, client, scope set, resource): every agent
approved through one pre-registered Device-flow client with the same scopes shared one grant, so the person saw one row
with the first agent's time, and revoking it cut off every agent behind it.

**Each approval is its own grant, it states when it was issued and to which client, and revoking it re-prompts.**

1. **One approval, one grant.** Every authorization-code redemption and device-code exchange starts a refresh family
   (SEC-0006 D9) and issues a new `AgentGrant` that the family's refresh tokens name. Revoking it fails that family's
   next refresh closed and leaves the person's other approvals refreshing; refresh-token reuse revokes only its own
   family's grant. The grant, not the refresh family, is the revocable unit because grants are SEC-0005's revocable,
   queryable record and the refresh token already names one; a family-as-unit would make consumers revoke a protocol
   artifact the server owns. A re-connect that remembered consent lets through is an approval too and issues its own
   grant rather than extending the one it matched: extending would put two refresh families behind one grant again,
   which is the defect this amendment removes. What a token may do is unchanged: its scopes are the consented ones, and
   the grant's resource is the OAuth resource URI, inert at the entity gates.
2. **Revoking re-prompts.** Remembered consent is no longer "any live grant for the key". It is a `RememberedConsent`
   row whose id hashes the subject, the capability term and the resource. A consent the person gives on the consent
   page writes it for the lifetime of the grant that consent issues; a re-connect it lets through does not extend it.
   The authorization server registers an `AgentGrant` before-remove hook that deletes the row whenever a live grant it
   issued is removed — by `Remove()`, a removal by id, the access explainer's revoke, erasure, or refresh reuse — so
   revoking any one approval brings back the consent page while the person's other approvals keep refreshing (SEC-0006
   D9: "a revoked grant re-prompts"). `Remove()` is therefore the revoke; there is no second call to learn. Removing an
   expired grant revokes nothing and forgets nothing. A revoke fails toward asking the person again: the hook forgets
   the consent before the grant is deleted, on a bounded token of its own (ten seconds) rather than the caller's, so a
   caller that closes the tab, or a delete that fails, can leave the grant but never the consent, and the next connection
   shows the consent page. If forgetting fails, the removal stops and the grant and consent both survive for a retry.
   Forgetting after the delete was rejected: a failure there would leave a consent no retry could reach, since the grant
   that fires the hook would be gone.
3. **When.** `AgentGrant.IssuedAt` (`DateTimeOffset?`) is when the grant was issued, named as its own `ExpiresAt` is.
   Koan's issuers set it: the authorization server, from the same clock reading as the grant's `ExpiresAt`, and
   `JitGrantService.GrantAsync`. Extending a JIT grant keeps it. An application that saves a grant itself sets it as it
   sets `ExpiresAt`; Koan does not stamp it on save, because a re-save of an older row could not tell its issue from now.
4. **Which client.** `AgentGrant.ClientId` and `AgentGrant.ClientName` (`string?`) are the `client_id` of the OAuth
   client the authorization server issued the grant to, and the name that client registered (RFC 7591 `client_name`)
   as it was at issue, or null when it registered none. The name is the client's own and unverified, as on the consent
   page. They are fields rather than an accessor over the capability term or the client's registration: the grant is
   the row a consumer queries and revokes, the fields need no parsing or second read, and they outlive a dynamic
   client's registration, which expires.
5. **Bounded live reads.** `AgentGrant.Approvals(subject, take)` answers the subject's unexpired grants that carry a
   `ClientId`, newest `IssuedAt` first, at most `take`. The gate's per-subject lookup (`AgentGrantStore`) asks for the
   subject's unexpired grants only. `AgentGrant` declares one composite index on `(Subject, ExpiresAt)`: a connector
   that builds it reads only the subject's live rows; one that does not reads more to answer the same — the in-memory
   connector scans, and SQL Server declines an index keyed on a `string`. The gate's lookup is not capped, since a cap
   would silently deny a live grant; it is bounded by the subject's live grants.
6. **Sweep.** The authorization server's cleanup service removes expired `RememberedConsent` rows and expired grants
   that carry a `ClientId`, beside the codes, requests and refresh tokens it already removes. It runs with no tenant in
   scope. Under `Koan.Tenancy`'s Closed posture every one of those entities needs a tenant, and Koan has no sanctioned
   scope for maintenance across tenants (`Tenant.None()` reaches only `[HostScoped]` entities), so the sweep cannot run
   there. In such a host the authorization server does not start at all today: its signing-key store reads a
   tenant-scoped entity with no tenant in scope. That gap is the authorization server's under tenancy, older than this
   amendment, and is reported for its own decision; this amendment does not claim the sweep in that host.
7. **Refresh tokens off.** With `EnableRefreshTokens = false` no token names a grant, so the server issues none and
   remembers no consent: every connection asks the person, and nothing is listed that removing would not end.
8. **The capability term is unchanged.** It still names the client and scope set inside the consent key; the new fields
   take no part in evaluation, materialization or revocation.

### Compatibility

- A grant issued before this amendment answers null for `IssuedAt`, `ClientId` and `ClientName`; `Approvals` leaves it
  out and the sweep does not remove it. It evaluates, refreshes and revokes as before, still backs every connection that
  shared it, and expires `RefreshTokenLifetime` after its issue. It has no `RememberedConsent` row, so it never skips
  the consent page: after upgrading, each client asks each person once more.
- An application that read a person's OAuth grants now sees one row per approval instead of one per (client, scope
  set, resource).
- Removing an `AgentGrant` now reads it first in a host that references the authorization server, for the remove hook.
- With refresh tokens off, the server no longer issues a grant or remembers consent.
- `IssuedAt`, `ClientId`, `ClientName`, `Approvals`, `RememberedConsent` and `AuthorizationCode.Remembered` are added,
  and the `(Subject, ExpiresAt)` index is declared; nothing public is removed or renamed.

### Evidence

- `GrantFactsSpec` (Koan.Web.Auth.Server.IntegrationTests):
  - A grant issued through the Device flow records `IssuedAt` between the request and the token,
    `ExpiresAt = IssuedAt + RefreshTokenLifetime`, its `ClientId` and the registered `ClientName`. A client that
    registered no name leaves `ClientName` null.
  - Two Device-flow approvals by one person through one client with the same scopes are two grants, each with its time
    and client. Revoking one fails its refresh with `invalid_grant` and leaves the other refreshing.
  - A person connects a code-flow client twice (asked, then remembered), revokes the first approval, and the next
    authorize shows the consent page while the second approval still refreshes.
  - An older grant and refresh token, saved as Koan stored them, refresh, do not skip the consent page, are not listed,
    and once removed fail the next refresh.
  - `Approvals` answers a person's live approvals newest first and bounded, without an expired approval, another
    person's, or a grant that is no approval. Among 40 expired grants the gate's lookup and `Approvals` find the one
    live grant; a sweep removes the expired approvals and an expired remembered consent and keeps a live approval and
    an expired grant that names no client.
- `GrantWithoutRefreshSpec` (same suite): with refresh tokens off, two connections are each asked and issue no grant.
- `RevokeCancelledSpec` (same suite): a revoke whose own token is cancelled after the server's hook has run, so the
  grant's delete fails, still leaves no consent remembered: the next authorize shows the consent page. It failed while
  the consent was forgotten after the delete.
- Failing before the fix: the Device-flow facts (null `IssuedAt` and `ClientId`), two approvals sharing one grant id,
  revoking one failing the other's refresh, the consent page skipped after revoking one approval, the older grant
  skipping the consent page, and the second connection skipped with refresh tokens off.
- `IdentityDayTwoSpec.Jit_grant_expires_extends_and_revokes` (Koan.Identity.Tests): a JIT grant records `IssuedAt`, has
  no client, and keeps `IssuedAt` when extended.
