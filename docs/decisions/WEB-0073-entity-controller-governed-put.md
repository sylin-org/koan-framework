# WEB-0073 — Governed PUT on the entity verb surface (+ the entity verb map)

- **Status:** Accepted — implemented (option 2: request-carried `RouteId`, applied by the endpoint service; PUT body id checked and normalized at the JSON level). Proven on a live host: PUT create-by-route-id, replace, `409` mismatch corrective, and the agent-race S01 battery 7/7 against a bare `EntityController<Recipe>;` — no delegator. Verb map shipped in `docs/capabilities/web/entity-api.md`; skill one-block updated (v5).
- **Date:** 2026-08-28
- **Deciders:** framework architect
- **Related:** [WEB-0069](WEB-0069-web-pipeline-contributors.md) (web pipeline contributors), [ARCH-0092](ARCH-0092-entity-exposure-surfaces.md) (entity exposure surfaces), SEC-0004 (gate·constrain·project — the Upsert choke point), `docs/capabilities/web/` (verb map), agent-race matrix `matrix/cells/test01-staged-composite/` (evidence origin).

## Context

Every REST-trained client — human or model — reaches for `PUT /{id}` to update. The governed surface deliberately did not expose it: writes flow through **POST-upsert** (`POST ""` → `EntityUpsertRequest` → `IEntityEndpointService.Upsert`), so full-replace semantics had no verb, and the update story required either reading source or hand-writing a delegating action on a subclass.

The agent matrix surfaced the cost precisely (`evals/agent-race/matrix/cells/test01-staged-composite/`): a frontier model (gpt-5.6-sol) spent exploration turns discovering the shape and hand-wrote the delegator correctly; a local 27B model (qwen38-27b, codex-OSS) **stalled entirely** on the question — researching the verb seam for its whole budget and never producing an application. The gap is a predictable, recurring cold-start tax on exactly the users the framework wants.

PATCH is **not** part of this gap: `[HttpPatch("{id}")]` already exists with content-type dispatch (RFC 6902 `json-patch`, RFC 7396 `merge-patch`, and partial-JSON — null semantics per content type), routing through the same governed choke point (`EndpointService.Patch`), with route-vs-payload id defense.

## Decision

1. **Add governed `PUT /{id}`** to `EntityController<TEntity, TKey>`:
   - Full-replace **shim over the same Upsert choke point** — no new pipeline, no second write path. The action packages the body into the identical `EntityUpsertRequest` the POST action builds.
   - **Route-id authority**: the route id is written onto the model before the request executes. A body carrying a non-default, *different* id is a client bug and fails correctively (`409 Conflict`, code `web.put.idMismatch`, both values named) — the verb-specific corrective, mirroring PATCH's id-mismatch defense.
   - Behavior is create-or-update when the id is absent/new, replace when it exists — delegated entirely to the existing Upsert semantics (validation, `[Access]`, hooks, stamps, audit, facts inherit).
   - `public virtual`, like every other action: present by default (flexibility is the point), overridable by teams that want the old surface.
2. **Document the entity verb map** in the capability leaf and skill: **POST = upsert · PUT = replace by id · PATCH = delta (content-type chooses the patch dialect) · DELETE = remove**. Until this ADR the update verb was discoverable only by reading source.

## Alternatives considered

- **Client-side delegator only** (status quo): rejected — it pushes governance-shaped boilerplate onto every team and, as measured, derails non-frontier agents entirely.
- **Opt-in attribute** (`[EnablePut]`): rejected — a capability that needs enabling contradicts Reference = Intent, and the shim adds no authority the POST action lacks.
- **Route-id silently wins on mismatch**: rejected — hides client bugs; the corrective failure names both ids.

## Open decision — the id-authority seam

`IEntity<TKey>.Id` is **get-only by contract**; only the concrete `Entity<T>` base exposes a setter, and the generic controller is constrained to the interface. Route-id authority therefore needs one of:

1. **Writable-interface**: add `IEntityWritable<TKey> : IEntity<TKey>` (`Id { get; set; }`) in `Koan.Data.Abstractions`; `Entity<T>` already satisfies it via its virtual setter. Controller: `if (model is IEntityWritable<TKey> w) w.Id = id;` — interface-first entities without it fall back to a corrective `501`-style response. *Most explicit; touches data abstractions.*
2. **Request-carried id**: add `TKey? RouteId { get; init; }` to `EntityUpsertRequest<TEntity>`; the endpoint service applies authority where it already reads `Model.Id` (the create-vs-update seam). *Smallest diff; authority lives in the service, matching PATCH (whose request carries the id).*
3. **Concrete-base cast** to `Entity<TEntity>`: rejected — breaks interface-first entities and leans on a self-referencing generic cast.

Recommendation: **option 2** — PATCH already proved the pattern (the request carries the id; the service applies it inside the governed choke point), and it adds no contract to the data abstractions.

## Consequences

- Every existing application gains `PUT /{id}` on its entity controllers. Teams that must not expose it override the action or gate it via the existing access/authorization surface (the shim enforces nothing new and bypasses nothing).
- The one-block skill skeleton and quickstart document the verb map; the hand-written PUT delegator pattern becomes obsolete.
- Proof: compile + PatchOps regression suite + the agent-race S01 battery (which exercises PUT create/update/persistence against a live host on every run).
