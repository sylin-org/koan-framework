# Sylin.Koan.Jobs.Testing

Drive the real Koan Jobs engine deterministically from xUnit, NUnit, MSTest, or a custom test runner.
The package contains no assertion framework and creates no second host.

```powershell
dotnet add package Sylin.Koan.Jobs.Testing
```

Configure the existing test host, then obtain the driver from its service provider:

```csharp
services.Configure<JobsOptions>(options =>
{
    options.EnableWorker = false;
    options.Mode = JobMode.Normal;
});

var driver = JobsTestDriver.From(host.Services);

await invoice.Job.Submit();
await driver.DrainAsync();
```

When a test owns a `FakeTimeProvider`, advance it and explicitly call `TriggerDueAsync`, `ReapAsync`,
or `DrainAsync`. `RunStageAsync` executes exactly one Entity-owned action and returns its production
`JobRunResult`, settled ledger record, and optional chain successor.

The driver intentionally exposes no orchestrator, scheduler, assertion API, host builder, clock, or
storage substitute. Misconfigured background or inline execution is rejected with the required
correction.
