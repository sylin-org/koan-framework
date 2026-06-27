using Newtonsoft.Json;
using Koan.Data.Core;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0007 Layer 1 — operator lifecycle ops: bulk suspend / reactivate (suspend ≠ delete; partial-failure
/// tolerant; one audit batch row records the operation intent) and lifecycle-aware delete (detect dependents →
/// cascade, so a dependent never raises a raw FK error).
/// </summary>
public sealed class IdentityLifecycleService
{
    public sealed record BulkResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);
    public sealed record DeleteReport(string IdentityId, int Emails, int Sessions, int Tokens, int ExternalLinks);

    public Task<BulkResult> SuspendAsync(IEnumerable<string> identityIds, CancellationToken ct = default)
        => SetStatusAsync(identityIds, IdentityStatus.Suspended, "identity.bulk_suspend", ct);

    public Task<BulkResult> ReactivateAsync(IEnumerable<string> identityIds, CancellationToken ct = default)
        => SetStatusAsync(identityIds, IdentityStatus.Active, "identity.bulk_reactivate", ct);

    private static async Task<BulkResult> SetStatusAsync(IEnumerable<string> identityIds, IdentityStatus status, string action, CancellationToken ct)
    {
        var succeeded = new List<string>();
        var failed = new List<string>();
        foreach (var id in identityIds)
        {
            try
            {
                var person = await Identity.Get(id, ct).ConfigureAwait(false);
                if (person is null) { failed.Add(id); continue; }
                person.Status = status;
                await person.Save(ct).ConfigureAwait(false);
                succeeded.Add(id);
            }
            catch
            {
                failed.Add(id);
            }
        }

        // One audit batch row for the operation as a whole (the per-entity Saves also each emit a row).
        await new AuditEvent
        {
            Action = action,
            Target = "Identity/*",
            After = JsonConvert.SerializeObject(new { succeeded, failed }),
        }.Save(ct).ConfigureAwait(false);

        return new BulkResult(succeeded, failed);
    }

    /// <summary>
    /// Delete a person and every dependent (emails, sessions, tokens, external links) — detect-then-cascade, so a
    /// dependent is never left to raise a raw FK error. Returns a report of what was removed.
    /// </summary>
    public async Task<DeleteReport> DeleteWithDependentsAsync(string identityId, CancellationToken ct = default)
    {
        var emails = await IdentityEmail.Query(e => e.IdentityId == identityId, ct).ConfigureAwait(false);
        var sessions = await Session.Query(s => s.IdentityId == identityId, ct).ConfigureAwait(false);
        var tokens = await ApiToken.Query(t => t.IdentityId == identityId, ct).ConfigureAwait(false);
        var links = await ExternalIdentityLink.Query(l => l.IdentityId == identityId, ct).ConfigureAwait(false);

        foreach (var e in emails) await e.Remove(ct).ConfigureAwait(false);
        foreach (var s in sessions) await s.Remove(ct).ConfigureAwait(false);
        foreach (var t in tokens) await t.Remove(ct).ConfigureAwait(false);
        foreach (var l in links) await l.Remove(ct).ConfigureAwait(false);

        var person = await Identity.Get(identityId, ct).ConfigureAwait(false);
        if (person is not null) await person.Remove(ct).ConfigureAwait(false);

        return new DeleteReport(identityId, emails.Count, sessions.Count, tokens.Count, links.Count);
    }
}
