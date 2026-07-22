---
type: SPEC
domain: framework
title: "R13-09 - Promote MongoDB Entity persistence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: in-progress
  scope: focused MongoDB provider, inert contract, pack, consumer, product, and API evidence
---

# R13-09 — Promote MongoDB Entity persistence

## Outcome

Promote `Sylin.Koan.Data.Connector.Mongo` as a supported 0.20 networked Entity provider and promote
its public dependency `Sylin.Koan.ZenGarden.Contracts` as the inert shared provider-author foundation
it already is. Split Couchbase from the former grouped demonstrated claim so neither provider inherits
the other's maturity.

## Architecture checkpoint

**Application intent:** An application installs the MongoDB connector, calls `AddKoan()`, and uses
normal `Entity<T>` save, query, paging, and streaming operations against a reachable MongoDB.

**Public expression:** The complete common expression is one connector package reference, ordinary
`AddKoan()`, an `Entity<T>` model, and either local discovery or standard Mongo configuration.

```csharp
builder.Services.AddKoan();

public sealed class Book : Entity<Book>;

var saved = await new Book().Save();
var same = await Book.Get(saved.Id);
```

**Guarantee/correction:** Mongo owns provider election, native filters, paging and provider-bounded
streams, batch/conditional writes, source pooling/routing, health, and declared isolation. An
unreachable selected service fails and reports unhealthy readiness; unsupported stream ordering
rejects before I/O. Explicit `zen-garden://` intent resolves through a ready runtime offering or fails
with correction. The transitive contracts alone activate nothing.

**Complete intent surface:** Connector package reference; `AddKoan()`; Entity statics; optional
existing configuration; reachable MongoDB; startup facts and health; the existing provider suite;
two dependency-closed artifacts; and one clean external package consumer. Applications do not need a
direct Contracts reference or Zen Garden runtime.

**Public concepts:** Existing `MongoOptions` owns deployment policy. Existing
`ZenGardenConnectionIntent` and related contracts form a module-free provider-author vocabulary
shared with future Ollama, S3, and Weaviate slices. Promotion adds no public API or runtime concept.

**Coalescence:** R13-07 PostgreSQL is the closest pattern: one connector plus one shared module-free
mechanism. The contract receives its own foundation claim because it has cross-provider meaning; it
does not belong semantically to Mongo. Keep the existing optional activation path and split the old
Mongo/Couchbase claim rather than granting group-wide support.

**Ergonomics:** The application references one package and retains normal Entity IntelliSense. The
transitive contract creates no action or runtime branch unless the application deliberately supplies
explicit layered-provider intent and the separate functional runtime.

## Evidence boundary

1. Run the complete existing MongoDB provider project against MongoDB 8.3.4.
2. Run focused connection-intent parsing and inert semantic-activation tests for the contract owner.
3. Pack the connector and contract with `PublicRelease=true`.
4. Restore, build, and run a clean external consumer from those packages plus NuGet.org; prove
   `AddKoan()` Entity behavior and absence of the optional Zen Garden runtime provider.
5. Compile product truth, run the API guard and cheap coherence; do not run sibling providers or
   framework-wide certification.

## Exit state

This card passes when both owners declare 0.20 intent, own honest supported claims, focused native,
contract, and package-consumer evidence passes, and both public artifacts are visible on NuGet.org.
Their exact first public versions become immutable API floors in the following slice.

## Focused evidence — 2026-07-22

- architecture and dependency closure: one functional connector plus one dependency-free,
  module-free contract owner;
- real provider boundary: existing Testcontainers fixture uses `mongo:8.3.4`;
- real MongoDB provider suite: 68/68 passed with zero skips against `mongo:8.3.4`;
- focused contract evidence: 45/45 connection-intent/FQID tests and 7/7 semantic-activation manifest
  tests passed with zero skips;
- `PublicRelease=true` packs: MongoDB connector and Zen Garden Contracts artifacts produced
  successfully at local first-publication version `0.20.0`;
- genuinely external staged-package consumer: clean restore, zero-warning Release build, real MongoDB
  boot, `AddKoan()` provider selection, Entity save/get/query, and absence of
  `IZenGardenInitializationProvider` all passed with `MONGODB|PACKAGE-CONSUMER|INERT-CONTRACT|PASS`;
- generated product truth, API posture, cheap coherence, publication, public indexing, and immutable
  baseline capture: pending.
