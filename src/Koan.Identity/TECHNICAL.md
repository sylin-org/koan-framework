# Sylin.Koan.Identity — technical contract

## Ownership

Identity owns the durable person, explicit provider links, cookie-session twin, global role binding, effective-access
contribution/explanation, identity-domain audit, lifecycle operations, and impersonation primitives. It does not own
external authentication protocols, OAuth client-token issuance, tenant membership, or HTTP projection.

`SecIdentityModule` registers typed options and host-owned services, discovers access contributors,
installs lifecycle audit hooks, replaces inert auth-store defaults with Entity-backed implementations, and applies the
startup posture. Package reference is activation intent; application code retains the ordinary `AddKoan()`.

## Sign-in and person reconciliation

When functional Web Auth is present, the discovered `IdentityAuthFlowHandler` runs late in its scoped lifecycle:

1. resolve an explicit `(provider, subject)` link or keep the incoming subject as the canonical person ID;
2. create the person or backfill only empty display fields;
3. attach/upgrade email factors without email-based account merging;
4. project global `IdentityRole` rows as standard role claims;
5. persist a `Session` and stamp its ID on the cookie principal.

Cookie validation rejects a missing/revoked stamped session, an inactive person, or an expired/revoked impersonation
grant. External identity persistence failures propagate through the Web Auth seam.

`IUserStore` and `IExternalIdentityStore` contracts live in inert `Sylin.Koan.Web.Auth.Abstractions`; Identity supplies
their durable implementations without activating functional Web Auth.

## Entity model

- `Identity`: canonical person and lifecycle status;
- `IdentityEmail`: normalized email factor associated with one person;
- `ExternalIdentityLink`: explicit provider-subject-to-person binding;
- `Session`: cookie-session/device twin and revocation state;
- `IdentityRole`: deterministic global person-to-standard-role binding;
- `AuditEvent`: best-effort identity mutation evidence, optionally hash chained;
- `ImpersonationGrant`: pending/approved/revoked, dual-control, time-boxed acting-as record.

These records are host/global-plane entities and deliberately exempt from tenant segmentation. Identity Tenancy owns
tenant-scoped membership and deprovisioning rather than introducing tenancy branches here.

## Access and impersonation

`EffectiveAccessResolver` orders discovered `IEffectiveAccessContributor` implementations and produces roles,
capabilities, source facts, and overlap warnings. `AccessExplainer.WhyAsync` projects contributing rows;
`CanAsync` calls the same `IAuthorize` floor used by production Web authorization. `RevokeAsync` removes supported
role/grant rows.

The authorization floor and `AgentGrant` currently live in `Sylin.Koan.Web`, so the headless Identity domain has an
explicit dependency on that package without exposing controllers. This is an honest current dependency, not a reason
to duplicate authorization contracts inside Identity.

Impersonation requires a reason, a different approver, and a bounded lifetime. The impersonated principal uses the
target as subject and preserves the operator in `koan_actor`; grant validation runs again during cookie validation.
Caller authorization remains the projection's responsibility.

## Lifecycle and audit

`SessionService` records, lists, and revokes cookie sessions. `IdentityLifecycleService` suspends/reactivates in
partial-failure-tolerant batches and deletes core-owned emails, sessions, external links, global roles, and
actor/target impersonation grants before deleting the person. Audit evidence is retained. Optional capability
dependents require their owning deprovisioning path.

Entity lifecycle hooks emit before/after snapshots for the owned domain entities. Raw provider claim blobs are
redacted. Emission is deliberately best-effort after the mutation; failures do not report the committed business
operation as failed. `HashChainAudit` serializes chain writes and allows `AuditChain.VerifyAsync` to detect content or
sequence changes, but it is not storage immutability.

## Configuration and reporting

`IdentityOptions` binds from `Koan:Identity`:

- `Posture`: nullable `IdentityPosture`; environment-derived when absent;
- `SeedDevUsers` / `DevUser`: Development + Open local-person seeding;
- `HashChainAudit`: opt-in tamper-evident audit chain.

Open outside Development fails startup. Module reporting exposes effective posture, source, durable reconciliation,
and the absence of a per-active-user licensing axis without exposing identity data.

## Unsupported and deferred

- bearer-token epoch revocation and personal access tokens;
- automatic email-based account merging;
- group-to-role/access semantics;
- transactional deletion across independent providers or optional modules;
- append-only storage enforcement, guaranteed audit delivery, or SIEM export;
- general UI, external IdP protocol handling, and OAuth authorization-server behavior;
- a framework-neutral authorization-floor extraction from the current Web package.
