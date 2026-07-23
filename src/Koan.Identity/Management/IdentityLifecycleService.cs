using Newtonsoft.Json;
using Koan.Data.Core;
using Koan.Identity.Erasure;
using Koan.Identity.Impersonation;
using Koan.Identity.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Koan.Identity.Management;

/// <summary>
/// SEC-0007 Layer 1 — operator lifecycle ops: bulk suspend / reactivate (suspend ≠ delete; partial-failure
/// tolerant; one audit batch row records the operation intent) and lifecycle-aware delete (detect dependents →
/// cascade, so a dependent never raises a raw FK error).
/// </summary>
public sealed class IdentityLifecycleService
{
    private readonly IReadOnlyList<IIdentityErasureContributor> _erasureContributors;
    private readonly ILogger<IdentityLifecycleService> _logger;

    /// <summary>
    /// Compatibility constructor for non-erasure lifecycle verbs. Resolve this service from DI to compose erasure
    /// owners; an uncomposed instance will return a corrective blocked erasure plan.
    /// </summary>
    [Obsolete("Resolve IdentityLifecycleService from dependency injection so identity erasure owners compose.")]
    public IdentityLifecycleService()
        : this(Array.Empty<IIdentityErasureContributor>(), NullLogger<IdentityLifecycleService>.Instance)
    {
    }

    public IdentityLifecycleService(
        IEnumerable<IIdentityErasureContributor> erasureContributors,
        ILogger<IdentityLifecycleService> logger)
    {
        _erasureContributors = erasureContributors
            .OrderBy(static contributor => contributor.Order)
            .ThenBy(static contributor => contributor.Owner, StringComparer.Ordinal)
            .ToList();
        _logger = logger;
    }

    public sealed record BulkResult(IReadOnlyList<string> Succeeded, IReadOnlyList<string> Failed);
    public sealed record DeleteReport(
        string IdentityId,
        int Emails,
        int Sessions,
        int ExternalLinks,
        int GlobalRoles,
        int ImpersonationGrants);

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

    /// <summary>Preview every registered erasure owner without changing data.</summary>
    public async Task<IdentityErasurePlan> PreviewErasureAsync(string identityId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityId);
        var duplicateOwners = _erasureContributors
            .GroupBy(static contributor => contributor.Owner, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static owner => owner, StringComparer.Ordinal)
            .ToList();
        if (duplicateOwners.Count > 0)
        {
            return new IdentityErasurePlan(
                DateTimeOffset.UtcNow,
                duplicateOwners.Select(owner => new IdentityErasureOwnerPlan(
                    IdentityErasureConstants.CompositionOwner,
                    int.MinValue,
                    Ready: false,
                    EstimatedItems: 0,
                    Summary: "Identity erasure composition is ambiguous.",
                    Correction: $"Keep exactly one contributor registered for owner '{owner}'.")).ToList());
        }

        if (_erasureContributors.Count == 0)
        {
            return new IdentityErasurePlan(
                DateTimeOffset.UtcNow,
                new[]
                {
                    new IdentityErasureOwnerPlan(
                        IdentityErasureConstants.CompositionOwner,
                        int.MinValue,
                        Ready: false,
                        EstimatedItems: 0,
                        Summary: "No identity erasure owner is active.",
                        Correction: "Reference Sylin.Koan.Identity and use AddKoan() so core erasure contributors are discovered."),
                });
        }

        var owners = new List<IdentityErasureOwnerPlan>(_erasureContributors.Count);
        foreach (var contributor in _erasureContributors)
        {
            try
            {
                var preview = await contributor.PreviewAsync(identityId, ct).ConfigureAwait(false);
                owners.Add(preview with { Owner = contributor.Owner, Order = contributor.Order });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Identity erasure preview failed for owner {Owner}.", contributor.Owner);
                owners.Add(new IdentityErasureOwnerPlan(
                    contributor.Owner,
                    contributor.Order,
                    Ready: false,
                    EstimatedItems: 0,
                    Summary: "Owner preview failed.",
                    Correction: $"Inspect protected logs for owner '{contributor.Owner}', correct it, and retry preview."));
            }
        }

        return new IdentityErasurePlan(DateTimeOffset.UtcNow, owners);
    }

    /// <summary>
    /// Execute every registered owner in deterministic order and persist a non-identifying integrity-checked receipt.
    /// Later owners still run after an owner-local failure so audit/privacy cleanup is not silently skipped.
    /// </summary>
    public async Task<IdentityErasureReceipt> EraseAsync(string identityId, CancellationToken ct = default)
    {
        var plan = await PreviewErasureAsync(identityId, ct).ConfigureAwait(false);
        if (!plan.CanComplete)
        {
            var corrections = string.Join(" ", plan.Owners
                .Where(static owner => !owner.Ready)
                .Select(static owner => owner.Correction ?? $"Owner '{owner.Owner}' is not ready."));
            throw new InvalidOperationException($"Identity erasure is blocked. {corrections}");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var results = new List<IdentityErasureOwnerResult>(_erasureContributors.Count);
        foreach (var contributor in _erasureContributors)
        {
            try
            {
                var result = await contributor.EraseAsync(identityId, ct).ConfigureAwait(false);
                results.Add(new IdentityErasureOwnerResult
                {
                    Owner = contributor.Owner,
                    Order = contributor.Order,
                    Succeeded = result.Succeeded,
                    Counts = new Dictionary<string, int>(result.Counts, StringComparer.Ordinal),
                    Summary = result.Summary,
                    Correction = result.Correction,
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Identity erasure failed for owner {Owner}.", contributor.Owner);
                results.Add(new IdentityErasureOwnerResult
                {
                    Owner = contributor.Owner,
                    Order = contributor.Order,
                    Succeeded = false,
                    Summary = "Owner erasure failed.",
                    Correction = $"Inspect protected logs for owner '{contributor.Owner}' and retry the same erasure request.",
                });
            }
        }

        var receipt = new IdentityErasureReceipt
        {
            PolicyVersion = IdentityErasureConstants.PolicyVersion,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            Complete = results.All(static result => result.Succeeded),
            Owners = results,
        };
        receipt.Hash = receipt.ComputeHash();
        return await receipt.Save(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Compatibility projection over the canonical erasure path. New code should retain the returned
    /// <see cref="IdentityErasureReceipt"/> from <see cref="EraseAsync"/>.
    /// </summary>
    public async Task<DeleteReport> DeleteWithDependentsAsync(string identityId, CancellationToken ct = default)
    {
        var receipt = await EraseAsync(identityId, ct).ConfigureAwait(false);
        if (!receipt.Complete)
            throw new InvalidOperationException(
                $"Identity erasure is incomplete. Inspect receipt '{receipt.Id}' and retry the erasure request.");

        var core = receipt.Owners.Single(owner => owner.Owner == IdentityErasureConstants.CoreOwner);
        return new DeleteReport(
            identityId,
            Count(core, IdentityErasureConstants.Counts.Emails),
            Count(core, IdentityErasureConstants.Counts.Sessions),
            Count(core, IdentityErasureConstants.Counts.ExternalLinks),
            Count(core, IdentityErasureConstants.Counts.GlobalRoles),
            Count(core, IdentityErasureConstants.Counts.ImpersonationGrants));
    }

    private static int Count(IdentityErasureOwnerResult result, string key)
        => result.Counts.TryGetValue(key, out var value) ? value : 0;
}
