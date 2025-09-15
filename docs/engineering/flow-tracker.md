# Flow tracker — tasks and status

Scope
- Track implementation steps for Koan.Flow to support entity-first pipelines and the Person example.

In progress / Done
- Runtime
  - InMemory runtime: Done
  - Dapr runtime: Replay/Reproject enqueues tasks: Done; future: workflow orchestration hooks
- Bootstrap/ops
  - Schema ensure: Done
  - TTL purge: Done
  - Projection worker: minimal materialization: Done; extend for canonical + lineage: In progress
- Web surface
  - Controllers for intake, views, lineage, policies, admin: Done
  - /views pagination/filter envelope: Done
- Docs
  - Flow reference + ADR: Done
  - Dapr notes + E2E try-it: Done

Next actions
1) Association/keying
   - Extract stable keys (from PolicyBundle.Content.keyTag) from normalized payloads
   - Upsert KeyIndex (single-owner), update ReferenceItem.RequiresProjection = true
   - Move processed records intake → keyed (CorrelationId = ReferenceId)
2) Projection reducer
   - Build canonical view (arrays of merged values) per tag path
   - Build lineage view (value → [sources])
   - Persist to per-view sets: canonical, lineage
3) Policy integration
   - Support minimal healing/synonyms in standardization step (from PolicyBundle)
4) E2E validation
   - Seed two inputs with differing department labels; expect canonical shows both; lineage splits by source

Notes
- Keep provider neutrality and first-class entity statics.
- No inline endpoints; controllers only.
