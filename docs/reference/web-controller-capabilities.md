## Web capability controllers (moderation, soft-delete, audit)

Contract
- Inputs: REST requests to canonical routes under api/{model}/...
- Outputs: 200 OK for list GET; 204 No Content for state changes; ProblemDetails for 400/404; 401/403 when unauthorized/forbidden.
- Options: set scoping via ?set= and payload fields TargetSet/FromSet as noted below.
- Error modes: not found ids, invalid body (400), unauthorized access (401/403), adapter limitations for filter-based operations.

Routes
- Moderation (requires registration via AddEntityModerationController<TEntity,TKey>)
  - POST {id}/moderation/draft — create draft (optionally from Snapshot)
  - PATCH {id}/moderation/draft — update draft
  - GET {id}/moderation/draft — fetch draft
  - POST {id}/moderation/submit — move Draft → Submitted
  - POST {id}/moderation/withdraw — move Submitted → Draft
  - GET moderation/queue — list submitted (paged, X-Total-Count headers)
  - POST {id}/moderation/approve — approve; optional Transform and TargetSet
  - POST {id}/moderation/reject — move to Denied with reason
  - POST {id}/moderation/return — move to Draft with reason

- Soft-delete (AddEntitySoftDeleteController<TEntity,TKey>)
  - GET soft-delete/deleted — list deleted (paged headers)
  - POST {id}/soft-delete — single; optional FromSet
  - POST soft-delete — bulk; ids or filter; optional FromSet
  - POST {id}/soft-delete/restore — single; optional TargetSet
  - POST soft-delete/restore — bulk; ids required; optional TargetSet

- Audit (AddEntityAuditController<TEntity>)
  - POST {id}/audit/snapshot — capture current state into Audit set (id#vN)
  - GET {id}/audit — list snapshots for id
  - POST {id}/audit/revert — revert to version; optional TargetSet

Payloads (DTOs)
- ApproveOptions: Transform (partial patch), TargetSet.
- RejectOptions: Reason (required).
- DraftCreate/DraftUpdate: Snapshot (partial/full).
- DraftSubmit/DraftWithdraw: reserved for future options.
- SoftDeleteOptions: FromSet; RestoreOptions: TargetSet.
- BulkOperation<TId>: Ids[], Filter (string-query), Options.
- AuditRevert: Version (int), TargetSet.

Paging and headers
- GET moderation/queue and GET soft-delete/deleted set pagination headers:
  - X-Total-Count, X-Page, X-Page-Size, X-Total-Pages.

Authorization
- All capability endpoints require [Authorize] by default.
- Swagger surfaces 401/403 responses for clarity; wire schemes via Koan.Web.Auth.* packages.

Edge cases
- Missing draft or snapshot → 404; empty bulk ids → 400; invalid filter when adapter doesn’t support string-query → 400; large result sets → prefer FirstPage/Page or stream APIs for data access.

Registration
- In Program.cs for each entity:
  - services.AddEntityModerationController<Article, string>();
  - services.AddEntitySoftDeleteController<Article, string>();
  - services.AddEntityAuditController<Article>();

Related
- Web conventions and controller guardrails: docs/api/web-http-api.md
- Data access and sets: docs/guides/data/all-query-streaming-and-pager.md, docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
- Authorization policy (fallback and defaults): docs/decisions/WEB-0047-capability-authorization-fallback-and-defaults.md
