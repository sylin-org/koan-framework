using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs;

namespace Koan.Mcp.Operations;

/// <summary>
/// P3.2 — the <c>jobs</c> operational toolset (opt-in via <c>Koan:Mcp:Operations:Jobs</c>). Governed agent verbs over
/// the job ledger: trigger an action, cancel a work item, read status. All require an <c>@ops:jobs</c> grant; the
/// mutating verbs audit; <c>cancel</c> is destructive (needs <c>confirm</c>). Wraps the string-keyed
/// <see cref="IJobCoordinator"/> directly (the generic <c>.Jobs</c> accessors are type-parameterized).
/// </summary>
[McpOperationalToolset("jobs")]
public sealed class JobsToolset : Toolset
{
    private readonly IJobCoordinator _jobs;

    public JobsToolset(IJobCoordinator jobs) => _jobs = jobs;

    [McpTool(Name = "koan.jobs.trigger", Description = "Trigger a job action at the type level for a work type (the on-demand twin of a scheduled tick). Requires an @ops:jobs grant.", IsMutation = true)]
    public async Task<object> Trigger(string workType, string action, ClaimsPrincipal? user, CancellationToken ct)
    {
        var subject = await OpsGate.RequireGrant(user, "jobs", ct);
        var handle = await _jobs.TriggerAsync(workType, action, ct);
        await OpsGate.Audit(subject, "jobs", "trigger", handle.JobId);
        return new { jobId = handle.JobId, workType, action };
    }

    [McpTool(Name = "koan.jobs.cancel", Description = "Durably cancel a work item's active job(s). Destructive — pass confirm:true. Requires an @ops:jobs grant.", IsMutation = true)]
    [McpDestructive]
    public async Task<object> Cancel(string workType, string workId, ClaimsPrincipal? user, CancellationToken ct, bool confirm = false)
    {
        var subject = await OpsGate.RequireGrant(user, "jobs", ct);
        if (!confirm) return OpsGate.DryRun($"durably cancel active job(s) for work type '{workType}', work id '{workId}'");
        await _jobs.CancelWorkAsync(workType, workId, ct);
        await OpsGate.Audit(subject, "jobs", "cancel", workId);
        return new { cancelled = true, workType, workId };
    }

    [McpTool(Name = "koan.jobs.status", Description = "Latest ledger status for a work item. Requires an @ops:jobs grant.")]
    [McpReadOnly]
    public async Task<object> Status(string workType, string workId, ClaimsPrincipal? user, CancellationToken ct)
    {
        await OpsGate.RequireGrant(user, "jobs", ct);
        var status = await _jobs.StatusAsync(workType, workId, ct);
        return new { workType, workId, status = status?.ToString() ?? "none" };
    }
}
