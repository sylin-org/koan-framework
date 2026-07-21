# Koan Golden Journey

This sample answers the question after FirstUse: does Koan stay simple when a useful application
gains business rules, durable work, an agent boundary, and an infrastructure mistake?

The domain is intentionally ordinary. A review request is opened, assessed in the background, and
offered to an agent for a bounded, non-final recommendation. The application owns those rules. Koan
owns persistence, job execution, HTTP/MCP hosting, composition, and runtime explanation.

Read the application in this order:

1. [`Domain/ReviewRequest.cs`](Domain/ReviewRequest.cs) — the business state and rules.
2. [`Web/ReviewsController.cs`](Web/ReviewsController.cs) — business-named HTTP actions.
3. [`Agent/ReviewTools.cs`](Agent/ReviewTools.cs) — bounded agent operations over the same rules.
4. [`Program.cs`](Program.cs) — the complete host bootstrap.

## Run it

```powershell
dotnet run --project samples/GoldenJourney
```

In another shell:

```powershell
$review = Invoke-RestMethod -Method Post -Uri http://localhost:5000/api/reviews -ContentType application/json -Body '{"title":"Review a critical production change","impact":2,"urgent":false}'
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/api/reviews/$($review.id)/assess"
Invoke-RestMethod "http://localhost:5000/api/reviews/$($review.id)/assessment"
Invoke-RestMethod "http://localhost:5000/api/reviews/$($review.id)"
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts
```

The printed URL is authoritative if it differs from `localhost:5000`. Assessment is asynchronous;
poll its status until it reports `Completed`.

## The six cumulative checkpoints

| Checkpoint | Meaningful addition | Application code remains about |
|---|---|---|
| V0 | Open and assess a request | The rule that determines priority |
| V1 | Save and retrieve it | The review request, not a repository |
| V2 | Offer useful HTTP actions | Opening and assessing, not generic CRUD plumbing |
| V3 | Run assessment durably | The work and its progress, not a queue implementation |
| V4 | Let an agent collaborate | Bounded discovery and a non-final recommendation |
| V5 | Explain and recover | A rejected adapter intent, its correction, and the restored election |

These are reading and verification checkpoints, not generated projects or scaffolding stages. Each
one preserves the previous result and adds one business-aligned capability.

## Reproduce V5: reject, explain, recover

Run the complete executable proof from the repository root:

```powershell
dotnet test tests/Koan.Packaging.Tests/Koan.Packaging.Tests.csproj --filter "FullyQualifiedName~SourceCheckoutProducesTheCumulativeGoldenJourney"
```

That command forces `Koan:Data:Sources:Default:Adapter=not-referenced` for one isolated start, checks
`/.well-known/Koan/facts` for `koan.data.adapter.rejected` / `adapter-unavailable` plus its correction,
then starts cleanly and confirms that the default election returns to SQLite.

To inspect the same behavior manually, start the rejected application:

```powershell
$env:Koan__Data__Sources__Default__Adapter = "not-referenced"
dotnet run --project samples/GoldenJourney
```

From another shell, use the URL printed by the app and inspect the facts:

```powershell
Invoke-RestMethod http://localhost:5000/.well-known/Koan/facts | ConvertTo-Json -Depth 8
```

Stop the app, remove the invalid intent, and restart:

```powershell
Remove-Item Env:Koan__Data__Sources__Default__Adapter -ErrorAction SilentlyContinue
dotnet run --project samples/GoldenJourney
```

This setting controls the application **default**, not every Entity. Koan resolves an ambient source
or adapter first, then an Entity's `[SourceAdapter]`/`[DataAdapter]`, then the configured Default, and
finally reference priority. `ReviewRequest` deliberately declares `[DataAdapter("sqlite")]`, so its
entity-specific route remains explicit while the invalid default is rejected rather than silently
replaced. V5 proves both facts: routing is scoped, and a bad default remains inspectable.

## What the executable contract proves

`GoldenJourneyContractTests` builds and runs this exact directory. It verifies SQLite persistence,
the business priority rule, completed job progress, selected Jobs ledger and transport, identical
operator/agent facts, MCP tool discovery, rejection before assessment, honest custom-tool dry-run,
caller-specific `koan://self` acknowledgement of both custom workflows, an agent recommendation
observed through REST, unavailable-adapter explanation followed by a clean restart, and the checked-in
composition lockfile. Package-only clean-room proof repeats the lockfile assertion after the Core
target flows transitively through `Sylin.Koan.App`.

This is not a production security template. Koan can supply the supported identity, authorization,
token, trust, and tenancy primitives selected by package reference. The application still owns its
policy declarations, exposed operations, credentials, HTTPS and network boundary, input validation,
backups, and deployment controls. HTTP MCP is enabled here only for local Development, and the agent
recommendation is deliberately non-final.
