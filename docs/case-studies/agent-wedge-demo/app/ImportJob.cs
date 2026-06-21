using Koan.Data.Core;        // .Save() entity extension
using Koan.Data.Core.Model;
using Koan.Jobs;
using Microsoft.AspNetCore.Mvc;

namespace Recs;

// Beat 5: a background job is just an entity that implements IKoanJob<T> with one static Execute.
// No queue, worker, or repository to wire — the ledger is the queue (in-memory tier by default, durable
// the moment a data adapter is present, which it is — postgres).
public sealed class ImportAnime : Entity<ImportAnime>, IKoanJob<ImportAnime>
{
    public string[] Titles { get; set; } = [];

    public static async Task Execute(ImportAnime job, JobContext ctx, CancellationToken ct)
    {
        foreach (var title in job.Titles)
            await new Anime { Title = title, Synopsis = "(imported by ImportAnime job)", Episodes = 12 }.Save();
    }
}

[Route("api/import")]
public sealed class ImportController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Import([FromBody] string[] titles)
    {
        var job = new ImportAnime { Titles = titles };
        await job.Job.Submit();                       // enqueue; runs in the background
        return Accepted(new { jobId = job.Id, queued = titles.Length });
    }
}
