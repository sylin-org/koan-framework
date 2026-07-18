# CustomerCanon

Turn messy customer arrivals into one durable, trusted customer. Repeated arrivals with the same
normalized email converge; invalid arrivals explain why they failed and never become canonical.

## Run

From the repository root:

```powershell
dotnet run --project samples/applications/CustomerCanon
```

Then run the requests in [requests.http](requests.http), or inspect:

- `GET http://localhost:5087/api/canon/models` — discovered model, route, pipeline, and identity key;
- `GET http://localhost:5087/.well-known/Koan/facts` — resolved composition;
- `GET http://localhost:5087/health/ready` — readiness.

The first request accepts deliberately messy casing and whitespace, normalizes it, derives a display
name and account tier, and persists one canonical customer under `.koan/data`. Sending it twice returns
the same canonical id. The invalid request returns `422` and leaves the stored customer count unchanged.

## Read the business

- `Customer : CanonEntity<Customer>` declares the canonical shape and uses email as its aggregation key.
- `CustomerPolicy` owns normalization, validation, display-name, and account-tier rules.
- `CustomerValidationContributor` and `CustomerEnrichmentContributor` are thin adapters from those rules
  to explicit Canon phases.

There is no Customer controller, pipeline registrar, or application module. `Program.cs` contains only
the ordinary `AddKoan().AsWebApi()` host. Referencing `Koan.Canon.Web` adds discovery, runtime composition,
and the HTTP surface; the JSON provider gives the sample deterministic local durability.

## Proven contract

The focused host test proves readiness, model/pipeline discovery, normalization, same-id convergence,
durable JSON storage, `422` rejection, no invalid persistence, and public facts:

```powershell
dotnet test tests/Suites/Samples/Koan.Samples.CustomerCanon.Tests
```

The test also proves that no admin/replay route is generated and that facts disclose Canon's ordered,
non-atomic commit. This sample does not claim distributed ingestion, locking, delivery, rollback,
blind-retry safety, or automatic recovery.
