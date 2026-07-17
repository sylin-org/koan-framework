# OrderIntake — one workload, one honest receipt

OrderIntake runs one bounded batch against a deliberately named data source, verifies every order, removes only
that trial's records, and keeps a durable receipt. The receipt describes this workload on this runtime; it does not
rank providers or recommend an application architecture.

## Reach the meaningful result

From the repository root:

```powershell
dotnet run --project samples/applications/OrderIntake
```

Open `http://localhost:5174`, leave `Local` selected, and run 100 orders. No Docker or external service is required.
The completed receipt must report equal requested, written, read, verified, and removed counts. The status remains
queryable after the workload data has been cleaned up.

The same path is scriptable:

```http
POST http://localhost:5174/api/trials/Local?count=100
```

Poll the returned `Location` until `status` is `Completed`. [`requests.http`](requests.http) contains the complete
request sequence.

## Read the application

- `TrialOrder : Entity<TrialOrder>` is the business payload.
- `OrderIntakeTrial : Entity<OrderIntakeTrial>, IKoanJob<OrderIntakeTrial>` owns write → verify → exact cleanup.
- `WorkloadTarget` names intent: `Local`, `Documents`, `Relational`, or `KeyValue`.
- `TrialsController` submits and observes the durable work item; Jobs owns queuing, progress, retries, and settlement.
- `appsettings.json` maps target names to providers. The job scopes only TrialOrder operations to the selected source,
  so the control ledger and receipt remain on local SQLite.

`Program.cs` remains the standard four-line host. There is no repository, provider switch, worker registration,
SignalR hub, direct driver code, or application-owned database tuning.

## Enable an optional target

Start only the service you intend to exercise:

```powershell
docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d mongo
docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d postgres
docker compose -f samples/applications/OrderIntake/docker/compose.yml up -d redis
```

Then select `Documents`, `Relational`, or `KeyValue`. Without that service, the trial fails durably and its status
returns the exact corrective command. Merely configuring these routes does not gate initial readiness; once a route
is selected it becomes an active dependency, so `health/ready` reports its failure honestly until the service is
available. The SQLite default and completed local receipts remain intact. Startup and `/.well-known/Koan/facts`
expose the default and configured source decisions.

## Honest boundary

Counts are capped at 1,000 and use bounded batch operations. Durations are descriptive observations without
warm-up control, statistical sampling, isolation from other machine activity, or cross-provider equivalence claims.
Use this application to understand named-source composition and durable business work—not to publish benchmarks.
