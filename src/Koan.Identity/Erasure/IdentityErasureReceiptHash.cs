using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace Koan.Identity.Erasure;

internal static class IdentityErasureReceiptHash
{
    private const char Separator = (char)0x1f;

    public static string Compute(IdentityErasureReceipt receipt)
    {
        var owners = receipt.Owners
            .OrderBy(static owner => owner.Order)
            .ThenBy(static owner => owner.Owner, StringComparer.Ordinal)
            .Select(owner => string.Join(Separator,
                owner.Owner,
                owner.Order.ToString(),
                owner.Succeeded.ToString(),
                owner.Summary,
                owner.Correction ?? "",
                string.Join(",", owner.Counts
                    .OrderBy(static count => count.Key, StringComparer.Ordinal)
                    .Select(static count => $"{count.Key}={count.Value}"))));

        var canonical = string.Join(Separator,
            receipt.PolicyVersion,
            receipt.StartedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            receipt.CompletedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            receipt.Complete.ToString(),
            string.Join(Separator, owners));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
