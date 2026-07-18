using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;

namespace SnapVault.Models;

/// <summary>
/// Integrity-checked record of one completed client-access closure. It chains the framework seat-removal receipt and
/// records the domain rows removed. It is not transactional, signed, append-only, or proof of current external state;
/// enforcement remains the missing grant and membership. <c>IAmbientExempt</c> keeps the operational record outside
/// the tenant relationship it describes.
/// </summary>
public sealed class ClientAccessClosureReceipt : Entity<ClientAccessClosureReceipt>, IAmbientExempt
{
    public string GuestIdentityId { get; set; } = "";
    public string StudioTenantId { get; set; } = "";
    public int GrantsRemoved { get; set; }
    public int SelectionsRemoved { get; set; }
    public string SeatReceiptId { get; set; } = "";
    public string SeatReceiptHash { get; set; } = "";
    public List<string> Surfaces { get; set; } = new();
    public string HashAlgorithm { get; set; } = "sha256-content";
    public DateTimeOffset OccurredAt { get; set; }
    public string Hash { get; set; } = "";

    public string ComputeHash() => DeterministicId.From(
        "client-access-closure-v1",
        GuestIdentityId,
        StudioTenantId,
        GrantsRemoved.ToString(),
        SelectionsRemoved.ToString(),
        SeatReceiptId,
        SeatReceiptHash,
        string.Join(",", Surfaces),
        HashAlgorithm,
        OccurredAt.ToUnixTimeMilliseconds().ToString());

    public bool HasValidHash() =>
        Hash.Length > 0 && string.Equals(Hash, ComputeHash(), StringComparison.Ordinal);
}
