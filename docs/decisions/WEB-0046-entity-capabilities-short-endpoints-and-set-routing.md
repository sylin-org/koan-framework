# Soft-delete visibility and fetch behavior

Preferred (set move):
- Soft delete moves items from root to the `deleted` set. Baseline reads (root) naturally exclude deleted items; no extra filters are required.

Alternate (inline flag compatibility):
- For models that keep a `Deleted` flag in the root set for compatibility, the extension must override baseline fetches to inject a default filter that excludes soft-deleted records, unless the client explicitly opts in (e.g., `includeDeleted=true`). Implement this via a dedicated controller extension or a transformer (WEB-0035) that amends incoming filters.
- When both a set and a flag exist, the set remains authoritative for storage isolation; the flag is an overlay for clients. Do not union sets implicitly in baseline views.
# Moderation workflow: roles and transitions

Roles/claims (policy-driven):
- Author: create/edit drafts; submit; view own drafts; cannot approve.
- Reviewer: view submitted queue; approve/reject (final or return-for-edits).
- Publisher (optional): perform final approvals or reopen final rejections.
- submit: `moderation.draft` → `moderation.submitted`.
- approve: `moderation.submitted` → root (or targetSet).
- reject (final: false): `moderation.submitted` → `moderation.draft` with notes/reason; author can edit and resubmit.
- reject (final: true): `moderation.submitted` → `moderation.denied` and lock submissions for that RefId until reopened.
- reopen: privileged action to move `moderation.denied` → `moderation.draft`.

Visibility:
- Default GET by id returns the published (root) version for everyone.
- Drafts/submissions are only visible via the moderation endpoints; authors see their own drafts; reviewers see the submitted queue.

# Module placement and separation of concerns

Place capability controllers, DTOs, policies, and constants in a dedicated web module to preserve separation of concerns:
- Project: `Koan.Web.Extensions`
- Structure:
	- Controllers/Moderation/EntityModerationController.cs
	- Controllers/SoftDelete/EntitySoftDeleteController.cs
	- Controllers/Audit/EntityAuditController.cs
	- Policies/AuthorizationPolicies.cs (role/claim names)
id: WEB-0046
slug: WEB-0046-entity-capabilities-short-endpoints-and-set-routing
domain: Web
status: accepted
date: 2025-08-28
title: Entity capabilities — short endpoints (moderation, soft-delete, audit) that expand baseline set routing

# Context

EntityController provides a consistent CRUD surface for domain entities. Koan supports “sets” (root, backup, moderation, deleted, audit, etc.) routed via the `set` query parameter. Several cross-cutting capabilities need concise, discoverable HTTP endpoints that semantically build on the baseline without inventing parallel APIs or path-encoded sets.

Capabilities in scope: moderation, soft delete, and audit browse/revert.

# Decision

Define standardized capability endpoints as short, action-like subresources under the entity collection, while preserving baseline semantics and set routing via `?set=`.

Principles
- Controllers only; no inline endpoints (see WEB-0035).
- Sets are routing parameters, not path segments. Keep `?set=` for parallel stores.
- Paths are short and meaningful; collections use nouns, actions use verbs.
- Bulk operations accept `ids[]` or `filter` (string DSL) in the body.
- Return 204 No Content for successful state transitions; 200 for GETs; 404 for missing sources; 207 Multi-Status is allowed for mixed bulk outcomes.
- Idempotent by default: repeating the same transition is a no-op (204) unless strict mode is requested.

Canonical routes

Moderation (roles, drafts, review, set taxonomy)
Purpose: enable authors to work on drafts that are private to them until submitted; reviewers approve/reject; everyone else sees the published version. Backed by named sets for storage and transitions, exposed as a workflow with clear roles and actions.

Roles (claims/policies)
- Author: create/edit drafts; submit; view own drafts overlay.
- Reviewer: view all submitted items; approve/reject; optionally request changes.
- Publisher (optional): final approval role if needed; otherwise Reviewer publishes.

Visibility and reads
- Default GET /api/{plural}/{id} returns the published (root) version for all.
- Moderation surfaces drafts and submissions via dedicated endpoints (below). No explicit `view=published` is needed; published is the root set.

Data model (envelope stored in moderation sets)
- Store drafts as envelopes in set=`moderation.draft` (per author), keyed by RefId (entity id) + DraftId (ULID) when needed.
- Envelope fields: RefId, DraftId, Status (Draft|Submitted|Approved|Rejected|Withdrawn), AuthorId, Snapshot (proposed entity), Diff (optional), Notes, Timestamps, ReviewerId.
- Only approved snapshots are published to root; all transitions are audited.

Set taxonomy and naming (recommended)
- moderation.draft: private author drafts (optionally partitioned by AuthorId)
- moderation.submitted: items awaiting review (proposed changes)
- moderation.approved: optional staging/log of approvals (may be ephemeral)
- moderation.denied: rejected submissions for author visibility/history
- moderation.audit: detailed moderation events and notes (optional; or use global audit)

Notes
- Published content resides in the root set (no suffix). Moderation sets are parallel stores that model workflow states.

Endpoints
- Authoring
	- POST  /api/{plural}/{id}/draft                → create draft from current (or new); body: partial or full snapshot (writes to moderation.draft)
	- PATCH /api/{plural}/{id}/draft                → update draft content (author only)
	- GET   /api/{plural}/{id}/draft                → get current draft (author or reviewer)
	- POST  /api/{plural}/{id}/draft/submit         → submit draft for review (move moderation.draft → moderation.submitted)
	- POST  /api/{plural}/{id}/draft/withdraw       → withdraw an open/submitted draft (author) (move back to moderation.draft or mark withdrawn)

- Review
	- GET   /api/{plural}/moderation/review-queue   → list submitted items (paged/filterable; reads from moderation.submitted)
	- GET   /api/{plural}/{id}/diff?source=draft&target=published → compute/display diff (draft vs root)
	- POST  /api/{plural}/{id}/moderate/approve     → publish submitted draft to targetSet (default root); optionally copy to moderation.approved
	- POST  /api/{plural}/{id}/moderate/reject      → reject with reason; (move moderation.submitted → moderation.denied)
	- POST  /api/{plural}/{id}/moderate/return      → non-final rejection, return to author for edits (move moderation.submitted → moderation.draft)

- Bulk (reviewer)
	- POST  /api/{plural}/moderation/approve        → body: { ids[] | filter, options }
	- POST  /api/{plural}/moderation/reject         → body: { ids[] | filter, reason }

Notes
- Draft creation supports new items (no published version) as well as edits.
- Approve publishes snapshot to the published store (root) and records an audit entry; optionally preserve a copy in moderation.approved; draft status becomes Approved and closed. When ApproveOptions.Transform is provided, apply it as a shallow JSON merge onto the draft snapshot before publish; Id must remain the approved entity id.
- Rejected drafts remain available to the author in moderation.denied; optional `requeue` moves moderation.denied → moderation.draft for resubmission.
- Concurrency: enforce ETag/version on publish to avoid overwriting newer published changes (409 on conflict, unless force).

Soft delete
- GET    /api/{plural}/deleted                      → alias of `GET /api/{plural}?set=deleted`
- DELETE /api/{plural}/{id}/soft                    → soft-delete single (move to deleted)
- POST   /api/{plural}/soft-delete                  → bulk soft-delete (ids[] or filter)
- POST   /api/{plural}/{id}/restore                 → restore single (from deleted to targetSet)
- POST   /api/{plural}/restore                      → bulk restore (ids[] or filter)
- Optional alias: DELETE /api/{plural}/{id}?soft=true (thin redirect to /soft)

Audit
- GET    /api/{plural}/{id}/audit                   → list snapshots (paged)
- GET    /api/{plural}/{id}/audit/{version}         → fetch specific snapshot
- POST   /api/{plural}/{id}/audit/revert            → revert to a given version (body)

Payloads (schemas)
- ApproveOptions: { targetSet?: string, note?: string, transform?: object }
- RejectOptions:  { reason: string, note?: string }
- DraftCreate: { snapshot?: object, note?: string }
- DraftUpdate: { snapshot?: object, note?: string }
- DraftSubmit: { note?: string }
- DraftWithdraw: { reason?: string }
- SoftDeleteOptions: { note?: string, fromSet?: string }
- RestoreOptions: { note?: string, targetSet?: string }
- BulkOperation<TId>: { ids?: TId[], filter?: string, options?: object }
- AuditRevert: { version: number, note?: string, targetSet?: string }

Security & metadata
- Capability endpoints require elevated roles/claims; record actor/time/reason in metadata via transformers/hooks (WEB-0035).
- Audit snapshots carry RefId, Version, At, By, and a full entity snapshot. Reverts write a new current version and naturally create a new audit entry.

Policy scaffolding
- Controllers are annotated with [Authorize]; concrete policies are resolved in the host app. Recommended names: `moderation.author`, `moderation.reviewer`, `moderation.publisher`, `softdelete.actor`, `audit.actor`.
- Apps wire these with role/claim requirements in their auth setup and can override per-route via attributes.
	- Infrastructure/CapabilityRoutes.cs and CapabilitySets.cs (constants)
	- Contracts/ModerationOptions.cs, SoftDeleteOptions.cs, RejectOptions.cs, BulkOperation.cs
- Dependencies: reference `Koan.Web` and `Koan.Data.Core`; avoid coupling back from Koan.Web to the extensions module.

# Scope
- Applies to generic capability controllers and sample typed controllers. Does not remove or change baseline CRUD endpoints.
- Consumes existing set routing (`DataSetContext`, storage-name suffixing) and first-class model statics.
 - Implementation resides in `Koan.Web.Extensions` to keep the base web module lean and focused.

# Consequences
- Uniform, terse URLs for common transitions. Discoverability via short subresources.
- No path-based set encoding; reduces route sprawl. Compatibility with existing clients using `?set=` remains.
- Enables bulk operations server-side without client-side fan-out.

# Implementation notes
- Add base controllers per capability (e.g., `EntityModerationController<TEntity, TKey>`); wire attribute routes above.
- Delegate moves/copies to data helpers (e.g., Data<TEntity, TKey>.MoveSet/CopySet) and instance `Save("set")`.
- Centralize set names and route segment constants (Infrastructure/Constants).
- Expose pagination headers on listing endpoints; support POST /query for advanced filters.
 - Read overlay: implement a resolver that, based on `view` and caller roles, returns published/draft/proposed snapshot without changing baseline published behavior for general clients.

# Examples

Approve one from moderation to root:
POST /api/items/123/moderate/approve
Body: { "note": "reviewed by A" }
→ 204 No Content

Soft-delete many by filter:
POST /api/items/soft-delete
Body: { "filter": "Status:inactive" }
→ 204 No Content

List deleted items (paged):
GET /api/items/deleted?page=1&size=50
→ 200 OK + pagination headers

Revert to a specific audit version:
POST /api/items/123/audit/revert
Body: { "version": 7, "note": "bad publish" }
→ 204 No Content

# References
- WEB-0035 — EntityController transformers
- docs/api/web-http-api.md — HTTP API conventions
- DATA-0030 — Entity sets routing and storage suffixing
- DATA-0062 — Instance Save(set) is first-class