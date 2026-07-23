A checkout should atomically persist the loan and accept a durable notice job for execution after its due date. Use the `koan-web` template, which includes SQLite, and add `Sylin.Koan.Jobs`.

```csharp
[JobPersistence(JobPersistenceMode.DataStore)]
[JobAction(SendNotice, MaxAttempts = 5, Timeout = "00:00:30")]
public sealed class Loan : Entity<Loan>, IKoanJob<Loan>
{
    public const string SendNotice = nameof(SendNotice);

    public string BookId { get; set; } = "";
    public string BorrowerId { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? ReturnedAt { get; set; }
    public DateTimeOffset? NoticeSentAt { get; set; }

    public static async Task Execute(
        Loan loan,
        JobContext context,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (loan.ReturnedAt is not null || loan.NoticeSentAt is not null)
            return;

        if (now < loan.DueAt)
        {
            context.Reschedule(loan.DueAt);
            return;
        }

        var notices =
            context.Services.GetRequiredService<IOverdueNoticeSender>();

        // The loan ID is the external idempotency key because Jobs is at-least-once.
        await notices.SendOnce(loan.Id, loan.BorrowerId, loan.BookId, ct);

        loan.NoticeSentAt = now; // Koan saves mutations made to the job Entity.
        await context.Progress(1, "Overdue notice sent");
    }
}
```

Expose the business actions directly:

```csharp
public sealed record CheckoutLoan(
    string BookId,
    string BorrowerId,
    DateTimeOffset DueAt);

[ApiController]
[Route("api/loans")]
public sealed class LoansController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<Loan>> Checkout(
        CheckoutLoan request,
        CancellationToken ct)
    {
        if (request.DueAt <= DateTimeOffset.UtcNow)
            return BadRequest("dueAt must be in the future.");

        var loan = new Loan
        {
            BookId = request.BookId,
            BorrowerId = request.BorrowerId,
            DueAt = request.DueAt
        };

        using (EntityContext.Transaction("checkout"))
        {
            await loan.Save(ct);

            await loan.Job.Submit(
                Loan.SendNotice,
                after: request.DueAt - DateTimeOffset.UtcNow);

            await EntityContext.Commit();
        }

        return AcceptedAtAction(nameof(Get), new { id = loan.Id }, loan);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Loan>> Get(string id, CancellationToken ct)
    {
        var loan = await Loan.Get(id, ct);
        return loan is null ? NotFound() : Ok(loan);
    }

    [HttpPost("{id}/return")]
    public async Task<ActionResult<Loan>> Return(string id, CancellationToken ct)
    {
        var loan = await Loan.Get(id, ct);
        if (loan is null)
            return NotFound();

        loan.ReturnedAt = DateTimeOffset.UtcNow;
        await loan.Save(ct);
        await loan.Job.Cancel();

        return Ok(loan);
    }

    [HttpGet("{id}/notice-status")]
    public async Task<ActionResult<JobStatus?>> NoticeStatus(
        string id,
        CancellationToken ct)
    {
        var loan = await Loan.Get(id, ct);
        return loan is null ? NotFound() : Ok(await loan.Job.Status());
    }
}
```

The host remains ordinary Koan composition:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();
builder.Services.AddScoped<IOverdueNoticeSender, EmailOverdueNoticeSender>();

var app = builder.Build();
await app.RunAsync();
```

SQLite owns durable loan and job-ledger storage; Jobs owns acceptance, deferred execution, retries, and inspection. The transaction prevents a persisted loan without its corresponding job. `[JobPersistence(DataStore)]` makes startup fail if a durable Data provider is missing. `Submit` confirms ledger acceptance, not delivery, and the application-owned sender must honor the loan ID idempotency key because execution is at-least-once. No repository, `DbContext`, hosted worker, queue registration, or Jobs-specific registration is required.
