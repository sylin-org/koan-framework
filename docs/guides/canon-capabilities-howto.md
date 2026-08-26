---
type: GUIDE
domain: canon
title: "Build a trusted canonical Entity"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-08-25
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-25
  status: passed
  scope: cold-executed against published packages (Sylin.Koan.Canon 1.0.7): create / merge /
    replay-idempotent / 422-refusal journey over HTTP plus code-read of runtime, matching,
    reconcile, persistence, and stage paths. Declarations on this page use the current language
    ([MatchKey] / [Reconcile] / OnIntake); Canon 1.0.12+ ships these spellings on nuget.org —
    older published 1.0.x packages spelled them [AggregationKey] / [AggregationPolicy(Kind)].
related_guides:
  - entity-capabilities-howto.md
---

# Build a trusted canonical Entity

Use Canon when multiple or imperfect arrivals must converge into trusted business state. The
application defines identity and rules. Koan discovers them and owns pipeline composition,
persistence, provenance, and optional Web exposure.

**Copy from here** (verified exemplar, kept compiling by the repo):

| Piece | Path |
|---|---|
| Canonical model | `samples/applications/CustomerCanon/Domain/Customer.cs` |
| Validation contributor | `samples/applications/CustomerCanon/Pipeline/CustomerValidationContributor.cs` |
| Enrichment contributor | `samples/applications/CustomerCanon/Pipeline/CustomerEnrichmentContributor.cs` |
| Host | `samples/applications/CustomerCanon/Program.cs` |

## 1. Add the capability

For a Web application, reference:

```powershell
dotnet add package Sylin.Koan.Canon.Web
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

The ordinary host remains ordinary:

```csharp
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

Referencing Canon is intent. Do not add a Canon registrar, application module, controller, or custom
runtime-registration call.

## 2. Define identity and conflict rules

```csharp
using Koan.Canon;

public sealed class Customer : CanonEntity<Customer>
{
    [MatchKey]
    public string Email { get; set; } = "";

    [Reconcile(Keep.Latest)]
    public string Phone { get; set; } = "";

    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string DisplayName { get; set; } = "";
}
```

- `[MatchKey]` properties are identity: two arrivals sharing a key value reconcile into the same
  canonical record.
- `[Reconcile(Keep.*)]` declares what wins when values conflict: `Keep.First`, `Keep.Latest`,
  `Keep.Min`, `Keep.Max`, or `Keep.From("billing")` for an authoritative source (falls back to
  latest-wins until that source contributes).
- Properties without an attribute also keep **newest-wins**. Declare a strategy only where newest is
  the wrong answer.
- Canon carries metadata, source attribution, lineage, lifecycle, and readiness alongside the Entity.

## 3. Prepare arrivals — `OnIntake`

Trimming, casing, defaults: override one virtual on the model. It runs first in Validation — before
user validators and before match keys are evaluated — so identity always sees prepared values:

```csharp
public sealed class Customer : CanonEntity<Customer>
{
    // ... properties above ...

    public override Customer OnIntake(Customer candidate)
    {
        candidate.Email = candidate.Email.Trim().ToLowerInvariant();
        candidate.FirstName = candidate.FirstName.Trim();
        candidate.LastName = candidate.LastName.Trim();
        return candidate;
    }
}
```

Rules owned outside the model register on the type gateway and run right after the override, in
registration order:

```csharp
using Koan.Core;

Person.Canon.OnIntake(p => p.DisplayName ??= $"{p.FirstName} {p.LastName}".Trim());
```

Grammar: base-form hooks (`OnIntake`) intervene before their moment; past-participle hooks
(`OnCommitted`, `OnParked`, `OnFailed`) observe after it. Registrations chain; operations terminate.

## 4. Validate and enrich — contributors

When logic needs services, emits structured rejection reasons, or is reused across models, write a
contributor. Discovery is automatic:

```csharp
using Koan.Canon;

public sealed class CustomerValidation : ICanonPipelineContributor<Customer>
{
    public CanonPipelinePhase Phase => CanonPipelinePhase.Validation;

    public ValueTask<CanonizationEvent?> Execute(
        CanonPipelineContext<Customer> context,
        CancellationToken cancellationToken)
    {
        var customer = context.Entity;
        if (customer.Email.Contains('@'))
        {
            return ValueTask.FromResult<CanonizationEvent?>(null);
        }

        return ValueTask.FromResult<CanonizationEvent?>(new CanonizationEvent
        {
            Phase = Phase,
            StageStatus = CanonStageStatus.Failed,
            Message = "Customer validation failed",
            Detail = "A valid email is required"
        });
    }
}
```

Koan runs `Intake`, `Validation`, `Matching`, `Reconcile`, `Projection`, then `Distribution`. Within
a phase, optional `Order` then type name make ordering deterministic. The first failed or parked
contributor stops the operation before later work or commit. A model with no application contributor
still receives built-in Matching and Reconcile behavior.

## 5. React to outcomes

Fan-out and side effects belong to outcome observers on the type gateway — they fire after the commit
checkpoints are done:

```csharp
using Koan.Canon;

Customer.Canon.OnCommitted(result => Console.WriteLine($"committed {result.Metadata.CanonicalId}"));
Customer.Canon.OnParked(result => reviewQueue.Enqueue(result));
Customer.Canon.OnFailed(result => alerting.Page(result.Events[^1].Detail));
```

## 6. Use and inspect it

With `Sylin.Koan.Canon.Web` referenced:

```http
POST /api/canon/customer
Content-Type: application/json

{
  "email": " Alice@Example.com ",
  "firstName": " Alice ",
  "lastName": " Example "
}
```

- `200` means the canonical Entity was materialized.
- `202` means the pipeline parked the arrival.
- `422` means a contributor rejected it; the response includes phase events and reasons.

Inspect `/api/canon/models` for the exact host model plan and `/.well-known/Koan/facts` for the runtime,
Web projection, selected Data provider, and non-atomic commit posture. For non-Web code, call
`await customer.Canonize()` within an active Koan host. Deferred arrivals stage instead:
`CanonStageBehavior.StageOnly` persists a receipt **and enqueues it** — the Jobs engine claims it,
re-enters the funnel at Intake (`OnIntake` and business rules apply), and settles the receipt by
outcome. A business-rule veto (`ctx.Hold(why)` in a contributor, or a registered `OnRule`) parks
the receipt as **Refused** at the vetoing phase; a mechanical block parks it as **Stalled**. Held
receipts wait in `Person.Canon.Hold`: `Hold.Counts.*` for the scoreboard, `Hold.Recover(...)`
to release — optionally repairing via the fixer hook; recovery always re-enters at Intake, because
a fix is a hypothesis, not a pass.

## Failure and operational boundaries

- Successful default commits write canonical Entity, match-key indexes, then audit. This sequence is
  ordered and fail-loud, but not atomic across all providers.
- A failed checkpoint can leave the earlier checkpoints durable. Canon names the checkpoint and does
  not promise rollback, blind-retry safety, or automatic recovery.
- Replace `ICanonPersistence` only when taking ownership of canonical, stage, and index operations as
  one unit. Replace `ICanonAuditSink` for audit delivery.
- A Canon phase event is not a Communication event or transport message.
- Canon Web generates model and inspection routes only. Headless rebuild is an application operation;
  replay, admin, rebuild, and value-object routes are not generated.
- The host's normal ASP.NET authentication and authorization policy applies to generated routes.

The complete runnable example is [CustomerCanon](../../samples/applications/CustomerCanon/README.md).
For all supported surfaces and limits, see the [Canon pillar reference](../reference/canon/index.md)
and the pipeline mechanics in [Canon pipeline](../capabilities/records/canon-pipeline.md).
