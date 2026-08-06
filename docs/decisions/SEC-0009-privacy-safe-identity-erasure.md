# SEC-0009 - Privacy-safe identity erasure with honest receipts

- **Status:** Accepted - implemented
- **Date:** 2026-07-22
- **Deciders:** framework architect
- **Amends:** [SEC-0007](SEC-0007-koan-identity-module.md) Layer 1 lifecycle and audit
- **Evidence:** external brownfield dogfooding

## Business intent

An application can erase a person once and trust that every participating owner closes access, removes or
anonymizes its personal data, sanitizes retained audit evidence, and reports an honest non-identifying outcome.

A consumer application may need two application-level moments: personal information disappears immediately, while a
separate non-identifying re-registration hold remains for a bounded period; final shell cleanup follows later. Koan
supplies the identity erasure primitive. It does not encode application-specific cooldown policy or claim that
application-owned data was handled unless that owner contributes to the operation.

## Context

`IdentityAuditHooks` currently registers unconditionally and serializes complete before/after Entity snapshots.
Those snapshots retain `Identity.DisplayName`/`Picture`, `IdentityEmail.Address`, session device metadata, roles,
provider-key hashes, and impersonation context. `IdentityLifecycleService.DeleteWithDependentsAsync` deletes the
core rows but deliberately retains those audit records. The result is a contradiction: the public lifecycle API can
remove the source records while Koan's own audit channel keeps reconstructable copies.

The same lifecycle crosses optional semantic owners. Identity Tenancy owns memberships and tenant-scoped grants;
applications own profiles, collections, business audit, and other domain rows. A central table scanner would couple
Identity to every package and application, while independent cleanup calls would recreate the remember-every-owner
failure.

The existing `IEffectiveAccessContributor` pipeline proves the composition shape, and the Identity Tenancy
deprovisioning receipt proves the value of an explicit result. Neither is sufficient unchanged: erasure requires a
preview, ordered execution, explicit incomplete outcomes, audit sanitization, and a receipt that does not itself
retain the subject.

## Decision

### D1. The existing lifecycle service is the one public entry point

The smallest honest application expression is:

```csharp
var plan = await identityLifecycle.PreviewErasureAsync(identityId, ct);
var receipt = await identityLifecycle.EraseAsync(identityId, ct);
```

`PreviewErasureAsync` is optional for callers but always runs internally before mutation. `EraseAsync` refuses a
blocked plan, executes every ready owner in deterministic order, continues after owner-local failures so later
privacy cleanup still runs, and returns `Complete=false` when any owner fails. Repeating the call is safe.

`DeleteWithDependentsAsync` remains as a compatibility surface and delegates to the same erasure path. There is no
second delete engine.

### D2. Semantic owners contribute, scanners do not discover data

`IIdentityErasureContributor` is a discovered, application-registerable seam. Each contributor has one stable owner
name, order, preview, and erase operation. Koan Identity supplies core-record and audit contributors. The optional
Identity Tenancy bridge contributes memberships, tenant grants, and its identity-bearing operational evidence.
Applications contribute only their own domain data through ordinary DI or an application `KoanModule`.

The coordinator rejects duplicate owner names correctively. A receipt proves only the owners listed in it. An absent
package or unregistered application owner is not silently certified.

### D3. Audit snapshots are privacy-safe by default

`IdentityAuditSnapshotMode.PrivacySafe` becomes the zero-configuration default. Snapshots retain only the minimum
non-identifying state transition useful for operations. Stable subject ids remain in the audit envelope until an
erasure request needs to sever that association.

`Full` remains an explicit forensic compatibility posture. Raw provider claim blobs remain redacted even in Full.
The effective snapshot posture and erasure availability appear in module reporting.

### D4. Erasure sanitizes related retained audit evidence

The audit contributor runs after data owners. It finds audit rows associated with the identity through the envelope
or serialized subject id and replaces identifying snapshots/context with an erasure marker, removes matching actor
and subject values, and retains only the target entity category. Action and occurrence time survive.

When audit chaining is active, sanitization first verifies the existing chain under the chain lock, rewrites the
authorized records, and re-hashes the chain from genesis. A pre-existing invalid chain blocks sanitization with a
corrective failure; erasure never blesses unexplained tampering. Unchained records are sanitized with explicitly
paged Entity reads.

The receipt reports the sanitization and re-chain outcome. It is not a cryptographic certificate or proof about an
external SIEM, backup, or system that did not contribute.

### D5. Receipts are useful without becoming a new identity record

`IdentityErasureReceipt` is an `IAmbientExempt` Entity containing:

- a random receipt id;
- policy version;
- start/completion timestamps;
- one result per owner with counts and a privacy-safe correction;
- complete/incomplete status; and
- a content hash for integrity checking.

It never stores the erased identity id, email, display name, provider key, or reversible lookup token. The caller
must retain the opaque receipt id when later retrieval is required. The content hash detects modification of the
record; it is not a signature or external attestation.

### D6. Failure is explicit and access closure comes first

The core contributor deactivates the person before removing sessions and dependents. A later failure therefore does
not restore ordinary cookie access. Owner failures are recorded without persisting exception text that may contain
personal data. Protected logs retain diagnostic detail.

Cancellation still propagates. A caller can repeat the same business request; owner operations are idempotent and a
new receipt records the new attempt. A complete receipt is emitted only when every participating owner reports
success.

## Complete intent surface

| Action | Public expression | Guarantee |
|---|---|---|
| Preview | `PreviewErasureAsync(identityId)` | Lists participating owners, estimated work, and blockers without mutation |
| Erase | `EraseAsync(identityId)` | Executes all participating owners and returns an honest receipt |
| Retry | Call `EraseAsync(identityId)` again | Idempotent owners converge after partial failure or restart |
| Retrieve | `IdentityErasureReceipt.Get(receiptId)` | Retrieves only the non-identifying outcome |
| Verify | `receipt.HasValidHash()` | Detects modification of the stored receipt fields |
| Extend | Implement/register `IIdentityErasureContributor` | Adds an application or optional-package semantic owner |

No new HTTP route is part of this decision. Identity.Web may project these verbs through controllers in a later
consumer-backed slice.

## Coalescence and placement

- **Keep:** Identity lifecycle as one named service; action/time audit evidence; Entity-backed operational receipt.
- **Absorb:** core cascade deletion, Identity Tenancy cleanup, and audit sanitization into one contributor fold.
- **Rebuild:** full-snapshot default, the one-shot delete report, and audit-chain mutation behavior.
- **Delete:** the claim that retained audit snapshots are inherently compatible with personal-data erasure.

Identity is the one target owner because the operation is about the durable person and its lifecycle. Core is too
wide, Data cannot know privacy meaning, provider adapters cannot coordinate semantic owners, and a consumer application
is too narrow to repair framework-owned audit rows.

## Ergonomics

The common path adds no configuration and no application inventory. Developers find preview/erase beside existing
suspend/reactivate lifecycle verbs. Contributor authors implement one domain-named seam and report their own counts.
The receipt makes partial work inspectable without teaching callers the underlying entity graph.

Concepts deliberately not added:

- no generic GDPR/legal-basis engine;
- no per-property erasure attributes;
- no reflection-based table scanner;
- no new event bus, scheduler, or privacy database;
- no claim of backup, external token, SIEM, or physical-media deletion without a participating owner;
- no claim that hashing an identity id anonymizes it.

## Consequences

### Benefits

- New applications do not leak identity PII into audit snapshots by default.
- Existing Full audit history can be sanitized without silently breaking its hash chain.
- Optional Koan packages and applications join one lifecycle without a central dependency graph.
- Operators receive exact owner-level outcomes instead of a success boolean.
- Consumer applications can preserve immediate-erasure and separate re-registration-hold policies without a private
  Koan workaround.

### Costs and limits

- Sanitizing a chained audit stream is an ordered rewrite and can be expensive; it is explicitly paged and serialized
  under the chain lock.
- The first implementation is synchronous and restart-safe by idempotent retry, not an automatically resumed durable
  saga. A Jobs projection requires a real long-running consumer.
- A receipt covers registered contributors only. Koan Web Auth Server, external IdPs, backups, and operator systems
  require their own future contributors or explicit application handling before an application can claim them.
- Authorized re-chaining preserves internal consistency but is not immutable external attestation.

## Acceptance

Owner-level tests must prove:

1. privacy-safe snapshots exclude display name, picture, email, provider claims/key, device/location, and
   impersonation reason/ticket markers;
2. explicit Full mode remains covered;
3. preview lists core and optional/application owners without mutation;
4. erasure removes core identity rows, Identity Tenancy rows, sessions, roles, external links, and grants;
5. every related retained audit field is marker-scanned after erasure;
6. chained audit verifies after authorized sanitization and refuses to sanitize an already-invalid chain;
7. a receipt contains no subject marker and validates its content hash;
8. one owner failure produces `Complete=false`, later owners still execute, and retry converges; and
9. the normal public lifecycle service is the only execution path.
