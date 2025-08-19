---
title: EntityController transformers (content-negotiated output/input)
status: accepted
date: 2025-08-17
---

Context

- We want entity-specific transformers that can shape GET output based on Accept headers and parse POST bodies based on Content-Type, without changing controller actions.
- Transformers must be grouped by entity (per-EntityController), declare supported media types, and be invoked before returning payloads (for GET) or before model binding (for POST).

Decision

- Introduce Sora.Web.Transformers module:
  - IEntityTransformer<TEntity, TShape> to declare Accept content types, Transform/TransformMany for output, and Parse/ParseMany for input.
  - ITransformerRegistry to register per-entity transformers and resolve by Accept/Content-Type with RFC 7231 q-values and wildcards.
  - MVC integration:
    - Output: global result filter that runs on controllers marked with [EnableEntityTransformers], inspects Accept, transforms TEntity or IEnumerable<TEntity>, and sets the response Content-Type.
    - Input: custom InputFormatter that recognizes registered Content-Types per entity and uses transformer.Parse* to produce TEntity or a list of TEntity for POST/PUT.
  - Registration helpers to add transformers via DI with their media types.

Details

- Contracts
  - IEntityTransformer<TEntity,TShape>
    - AcceptContentTypes: string[] of media types this transformer supports (e.g., text/csv).
    - TransformAsync/TransformManyAsync for output, ParseAsync/ParseManyAsync for input.
  - ITransformerRegistry
    - Register(...), ResolveForOutput(...), ResolveForInput(...), GetContentTypes<TEntity>().
- Matching rules (output)
  - Accept header is parsed per RFC 7231 including q-values and wildcards (type/*, */*).
  - Selection tie-breakers: higher q-value, then higher registration priority (Explicit via DI > Discovered), then header order.
- Registration & discovery
  - DI helper: services.AddEntityTransformer<TEntity,TShape,TTransformer>(params string[] contentTypes).
  - Auto-discovery (default on) via ISoraInitializer scans loaded assemblies for IEntityTransformer<,> and registers with Discovered priority.
  - DI registrations override discovered transformers for the same media type.
- MVC integration
  - Output: a global result filter checks for [EnableEntityTransformers] on the controller, resolves the best transformer for the response model type and Accept header, transforms payload, and sets Content-Type.
  - Input: a custom input formatter participates for entity types and uses ResolveForInput based on the request Content-Type.
- Swagger/OpenAPI
  - In Sora.Web.Swagger, a reflection-based operation filter (added only when transformers are present) advertises alternate media types:
    - For 200 responses on entity-returning actions.
    - For request bodies on actions taking entity parameters.
- Configuration
  - Sora:Web:Transformers:AutoDiscover (bool, default true): enable/disable transformer discovery.
  - Swagger gating is handled by Sora.Web.Swagger (Dev on by default; in non-Dev require Sora__Web__Swagger__Enabled=true or Sora:AllowMagicInProduction=true).

Consequences

- Positive:
  - Leverages ASP.NET Core content negotiation; controllers stay simple.
  - Clear per-entity scoping; multiple media shapes can coexist with q-value preference and DI precedence.
  - Works for both single and collection results.
- Negative:
  - TransformMany could be costly on large pages; streaming not yet implemented.
  - Input path currently requires exact Content-Type match (no wildcards) which is typical for parsers.

Alternatives considered

- Static [Produces]/[Consumes] attributes only: too static and per-action; doesn’t scale to per-entity shapes and discovery.
- Formatter-only approach: makes output path trickier and spreads logic across formatters; we prefer explicit per-entity transformers.
- Action filters per controller: feasible, but registry + attribute keeps concerns centralized and discoverable.

Adoption notes (usage)

- Mark your controller with the attribute:
  - [EnableEntityTransformers]
- Register a transformer (DI override takes precedence over discovery):
  - services.AddEntityTransformer<MyEntity, string, MyCsvTransformer>("text/csv");
- At runtime:
  - GET with Accept: text/csv returns CSV.
  - POST with Content-Type: text/csv is parsed via Parse*/ParseMany*.

Follow-ups

- Add CSV and DTO projection sample transformers in samples.
- Add q-value tests and Swagger coverage tests.
