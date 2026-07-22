using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Erasure;

namespace Koan.Identity.Tests;

/// <summary>Proves an application can join erasure with ordinary DI and retry an owner-local failure.</summary>
public sealed class TestIdentityErasureContributor : IIdentityErasureContributor
{
    private static readonly ConcurrentDictionary<string, byte> Failures = new(StringComparer.Ordinal);

    public const string OwnerName = "Test.Application";
    public string Owner => OwnerName;
    public int Order => 500;

    public static void FailNextErasure(string identityId) => Failures[identityId] = 0;

    public async Task<IdentityErasureOwnerPlan> PreviewAsync(string identityId, CancellationToken ct = default)
    {
        var rows = await TestIdentityErasureRow.Query(row => row.IdentityId == identityId, ct).ConfigureAwait(false);
        return new IdentityErasureOwnerPlan(Owner, Order, true, rows.Count, "Erase application-owned profile data.");
    }

    public async Task<IdentityErasureOwnerResult> EraseAsync(string identityId, CancellationToken ct = default)
    {
        if (Failures.TryRemove(identityId, out _))
            throw new InvalidOperationException("Deliberate application-owner failure for retry acceptance.");

        var rows = await TestIdentityErasureRow.Query(row => row.IdentityId == identityId, ct).ConfigureAwait(false);
        var removed = 0;
        foreach (var row in rows)
            if (await row.Remove(ct).ConfigureAwait(false)) removed++;
        if (removed != rows.Count) throw new InvalidOperationException("Application-owned erasure was incomplete.");

        return new IdentityErasureOwnerResult
        {
            Owner = Owner,
            Order = Order,
            Succeeded = true,
            Summary = "Application-owned profile data erased.",
            Counts = new Dictionary<string, int>(StringComparer.Ordinal) { ["profile-rows"] = removed },
        };
    }
}

public sealed class TestIdentityErasureRow : Entity<TestIdentityErasureRow>, IAmbientExempt
{
    public string IdentityId { get; set; } = "";
    public string PersonalValue { get; set; } = "";
}
