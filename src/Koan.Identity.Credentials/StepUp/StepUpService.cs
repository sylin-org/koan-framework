using Microsoft.Extensions.Options;
using Koan.Data.Core;

namespace Koan.Identity.Credentials.StepUp;

/// <summary>
/// SEC-0007 P3-grp4 — evaluates a person's outstanding step-up requirements against the factors proven this round,
/// and mints / redeems the single-use resume ticket. Free of HTTP — the orchestration is testable offline.
/// </summary>
public sealed class StepUpService
{
    private readonly IEnumerable<IStepUpRequirementContributor> _contributors;
    private readonly StepUpOptions _options;

    public StepUpService(IEnumerable<IStepUpRequirementContributor> contributors, IOptions<StepUpOptions> options)
    {
        _contributors = contributors;
        _options = options.Value;
    }

    /// <summary>The requirement factors for <paramref name="identityId"/> NOT yet satisfied by <paramref name="provenMethods"/>.</summary>
    public async Task<IReadOnlyList<StepUpRequirement>> UnsatisfiedAsync(string identityId, IReadOnlySet<string> provenMethods, CancellationToken ct = default)
    {
        var reqs = new List<StepUpRequirement>();
        foreach (var c in _contributors)
            reqs.AddRange(await c.RequiredForAsync(identityId, ct).ConfigureAwait(false));
        return reqs.Where(r => !r.SatisfiedBy.Any(provenMethods.Contains)).ToList();
    }

    /// <summary>Mint a single-use resume ticket for an interrupted sign-in.</summary>
    public Task<StepUpTicket> IssueTicketAsync(string identityId, IReadOnlySet<string> proven, IEnumerable<string> pendingFactors, CancellationToken ct = default)
        => new StepUpTicket
        {
            IdentityId = identityId,
            Satisfied = proven.ToList(),
            Pending = pendingFactors.ToList(),
            ExpiresAt = DateTimeOffset.UtcNow.Add(_options.TicketLifetime),
        }.Save(ct);

    /// <summary>Resolve a still-redeemable ticket by id, or null (missing / expired / already consumed).</summary>
    public async Task<StepUpTicket?> ResolveTicketAsync(string ticketId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ticketId)) return null;
        var ticket = await StepUpTicket.Get(ticketId, ct).ConfigureAwait(false);
        return ticket is not null && ticket.IsRedeemable(DateTimeOffset.UtcNow) ? ticket : null;
    }

    /// <summary>
    /// Consume a ticket (single-use). Returns false if it was already consumed / expired, OR if a concurrent consumer
    /// won the compare-and-set — so exactly one of two racing redemptions of the same ticket succeeds. The web
    /// factor-challenge controller must treat a false return as a hard reject (no resumed sign-in).
    /// </summary>
    public async Task<bool> ConsumeAsync(StepUpTicket ticket, CancellationToken ct = default)
    {
        if (!ticket.IsRedeemable(DateTimeOffset.UtcNow)) return false;
        ticket.ConsumedAt = DateTimeOffset.UtcNow;
        return await AtomicSingleUse.TryAsync<StepUpTicket, string>(ticket, t => t.ConsumedAt == null, ct).ConfigureAwait(false);
    }
}
