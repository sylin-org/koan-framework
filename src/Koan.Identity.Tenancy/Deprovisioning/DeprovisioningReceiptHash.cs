using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Koan.Identity.Tenancy.Deprovisioning;

/// <summary>
/// The canonical content hash for a <see cref="DeprovisioningReceipt"/> — a stable, precision-safe serialization
/// (the instant is hashed as unix-milliseconds so it round-trips identically across data adapters) over SHA-256.
/// </summary>
internal static class DeprovisioningReceiptHash
{
    public static string Of(
        string identityId, string? tenantId, DeprovisioningKind kind,
        int sessionsRevoked, int membershipsRemoved, string? statusSet,
        IEnumerable<string> surfaces, DateTimeOffset occurredAt)
    {
        var canonical = string.Join('\n',
            identityId,
            tenantId ?? "",
            kind.ToString(),
            sessionsRevoked.ToString(CultureInfo.InvariantCulture),
            membershipsRemoved.ToString(CultureInfo.InvariantCulture),
            statusSet ?? "",
            string.Join(',', surfaces),
            occurredAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
