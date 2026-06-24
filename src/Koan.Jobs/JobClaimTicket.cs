using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>
/// A claim ticket in the GUIDv7 "bakery" election (<see cref="ClaimStrategy.Ticket"/>, JOBS-0005 §7). Stored as a
/// parallel <see cref="Entity{T}"/> set in the same store, so every distributed worker queries the same contender
/// list ("list all tickets for job X" = <c>JobClaimTicket.Query(t =&gt; t.JobId == X)</c>). The smallest <c>Id</c>
/// (a time-ordered GUIDv7) wins. Tickets are GC'd once the claim resolves.
/// </summary>
public sealed class JobClaimTicket : Entity<JobClaimTicket>, IAmbientExempt
{
    public string JobId { get; set; } = "";
    public string Owner { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
