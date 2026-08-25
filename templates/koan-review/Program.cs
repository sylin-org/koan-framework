using Koan.AI.Review;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
builder.Services.AddKoanReview(review => review.Queue<ArticleSummary>(
    "summary-review",
    queue => queue
        .Where(item => item.ReviewStatus == ReviewStatus.Pending)
        .Display(item => new { item.Title, item.GeneratedSummary })
        .Approve()
        .Reject(requireReason: true)
        .Edit(item => item.GeneratedSummary)
        .Label(item => item.Category, "tech", "farm")
        .Flag("hallucination")));

var app = builder.Build();

// The queue is a typed projection over ordinary Entities. Decisions are applied through
// IReviewActionHandler and persisted with Save(); your public reads filter on the reviewed state.
app.MapGet("/api/queue/pending", async (CancellationToken ct)
    => Results.Json(await ArticleSummary.Query(s => s.ReviewStatus == ReviewStatus.Pending, ct)));
await app.StartAsync();
var handler = app.Services.GetRequiredService<IReviewActionHandler>();
var registry = app.Services.GetRequiredService<ReviewQueueRegistry>();

await ArticleSummary.RemoveAll();

// Composition: the queue is registered and carries its declared actions.
var queue = registry.Get<ArticleSummary>("summary-review")
    ?? throw new InvalidOperationException("queue 'summary-review' missing");
Console.WriteLine($"PROBE queue={queue.Name} actions={queue.Actions.Count}");

// Behavior: approve persists.
var keep = await new ArticleSummary { Title = "Bee Wrapping", GeneratedSummary = "Wrap hives after first frost." }.Save();
await handler.ApproveAsync(keep, "leo");
await keep.Save();

// Behavior: rejection records the reason.
var drop = await new ArticleSummary { Title = "Fig Repotting", GeneratedSummary = "Figs need bigger pots." }.Save();
await handler.RejectAsync(drop, "leo", reason: "summary invents a fertilizer claim");
await drop.Save();

// Correction: a name can be claimed once per registry - declaring it twice is refused.
try
{
    var fresh = new ReviewQueueRegistry();
    var duplicate = Review.Create<ArticleSummary>("summary-review",
        where: s => s.ReviewStatus == ReviewStatus.Pending,
        display: s => new { s.Title },
        actions: [Review.Approve<ArticleSummary>()]);
    fresh.Register(duplicate);
    fresh.Register(duplicate);
    Console.WriteLine("PROBE FAIL: duplicate queue name was accepted");
    return 1;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"PROBE correction: {ex.Message}");
}

var reloadedKeep = await ArticleSummary.Get(keep.Id);
var reloadedDrop = await ArticleSummary.Get(drop.Id);
if (reloadedKeep is null || reloadedDrop is null
    || reloadedKeep.ReviewStatus != ReviewStatus.Approved || reloadedDrop.ReviewStatus != ReviewStatus.Rejected)
{
    Console.WriteLine("PROBE FAIL: decisions did not persist");
    return 1;
}

Console.WriteLine($"PROBE approved={reloadedKeep.ReviewStatus} by={reloadedKeep.ReviewedBy}; rejected reason='{reloadedDrop.RejectionReason}'");
Console.WriteLine("PROBE PASS");

await app.StopAsync();
return 0;

public sealed class ArticleSummary : Entity<ArticleSummary>, IReviewable
{
    public string Title { get; set; } = "";
    public string GeneratedSummary { get; set; } = "";
    public string? OriginalGeneratedSummary { get; set; }
    public string Category { get; set; } = "";
    public List<string>? Flags { get; set; } = [];

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
}
