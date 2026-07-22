---
type: SPEC
domain: framework
title: "R13-10 - Promote Couchbase Entity persistence"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  status: passed
  scope: Couchbase published, indexed, public-consumer green, and baseline-captured
---

# R13-10 — Promote Couchbase Entity persistence

## Outcome

Promote `Sylin.Koan.Data.Connector.Couchbase` as a supported 0.20 networked Entity provider. Capture
the exact `0.20.1` API floors of the already-published MongoDB and Zen Garden Contracts packages in
the same slice; that baseline bookkeeping does not broaden Couchbase's guarantee.

## Architecture checkpoint

**Application intent:** An application installs the Couchbase connector, calls `AddKoan()`, and uses
normal `Entity<T>` persistence against a reachable Couchbase cluster.

**Public expression:** The complete common expression is one connector package reference, ordinary
`AddKoan()`, an `Entity<T>` model, and either local discovery or standard Couchbase configuration.

```csharp
builder.Services.AddKoan();

public sealed class Product : Entity<Product>
{
    public string Name { get; set; } = "";
}

var saved = await new Product { Name = "Garden sensor" }.Save();
var same = await Product.Get(saved.Id);
```

**Guarantee/correction:** Couchbase owns provider election, parameterized native filters, explicit
paging and provider-bounded streams, conditional replace, transactional batches, source-aware
cluster pooling/routing, readiness, and its declared isolation modes. An unavailable selected cluster
fails at selected use and reports unhealthy readiness; unsupported stream ordering rejects before
provider I/O. Fast-remove, snapshot streams, resumability, mutation-safe traversal, and universal
native-query parity are not claimed.

**Complete intent surface:** Connector package reference; `AddKoan()`; Entity statics; optional
existing connection, bucket, credential, scope, collection, durability, and source configuration; a
reachable Couchbase; package-owned startup/health evidence; and one clean package-only consumer. No
repository abstraction, manual module registration, or companion Koan package is required.

**Public concepts:** Existing `CouchbaseOptions` owns deployment policy. Existing Entity and Data
capability contracts own application behavior. Promotion adds no public API or runtime concept.

**Coalescence:** R13-09 MongoDB is the closest promotion pattern. Keep Couchbase as one adapter-level
owner and promote its already-separated claim. Reuse its existing test fixture, conformance suite,
package owner, product compiler, and publisher; add no provider workflow, admission coordinator, or
framework-wide certification path.

**Ergonomics:** The application references one discoverable connector and retains normal Entity
IntelliSense. Configuration is optional until placement, credentials, or native policy requires it;
the common path has no hidden registration step or extra conceptual branch.

## Evidence boundary

1. Run the complete existing Couchbase provider project against Couchbase Community 8.0.2.
2. Pack the connector with `PublicRelease=true`.
3. Restore, build, and run a clean external consumer from the staged package plus NuGet.org; prove
   `AddKoan()` selection and Entity save/get/query against a real container.
4. Compile product truth, run the API guard and cheap coherence; do not run sibling providers or
   framework-wide certification.
5. After main publication, observe the exact public package and rerun the consumer from NuGet.org.

## Exit state

This card passes when Couchbase declares 0.20 intent, owns an honest supported claim, focused native
and package-consumer evidence passes, and its public artifact is visible on NuGet.org. Its exact first
public version becomes the immutable API floor in the following slice.

## Focused evidence — 2026-07-22

- architecture and dependency closure: one connector whose public Koan dependencies are already
  supported foundations;
- real provider boundary: the assembly-shared fixture uses `couchbase:community-8.0.2`;
- complete Couchbase provider suite: 20/20 passed with zero skips in 7 minutes 21 seconds, covering
  CRUD, filter convergence, provider-bounded streaming, conditional replace, AODB isolation,
  source-provider deduplication, identifier safety, and participation-aware health;
- the duration is dominated by repeated fresh selected-use query/index readiness and is not added to
  the main PR gate;
- MongoDB and Zen Garden Contracts exact `0.20.1` API floors: recorded in their owning projects;
- `PublicRelease=true` pack: succeeded at local first-publication version `0.20.0`;
- genuinely external staged-package consumer: restored the connector exactly as `0.20.0` from the
  isolated feed plus public NuGet dependencies, built with zero warnings/errors, selected Couchbase
  through `AddKoan()`, and completed Entity save/get/query against Community 8.0.2 with
  `COUCHBASE|PACKAGE-CONSUMER|PASS`;
- generated product truth: 37 claims / 93 packages and the Couchbase owner is in the supported 0.20
  closure;
- API posture: 47/48 configured, with Couchbase as the sole allowed first-publication pending floor
  and three content-only owners;
- product-surface generated-drift check, the no-tests repository coherence pass, and the 36-row
  surface ledger: passed; no sibling provider tests or whole-framework certification ran;
- PR `#99` exact-head gate `29898149195`: passed and merged as
  `38d00f841b9dcd0cc22e3540918436e8d2f542d3`;
- main publication run `29898380061`: passed and pushed Couchbase `0.20.1`;
- NuGet.org indexed the exact artifact; a genuinely external NuGet.org-only application restored it,
  built with zero warnings/errors, selected Couchbase through `AddKoan()`, and completed Entity
  save/get/query against `couchbase:community-8.0.2` with `COUCHBASE|PUBLIC-CONSUMER|PASS`;
- the following R13-11 slice records exact `0.20.1` as the immutable API floor.
