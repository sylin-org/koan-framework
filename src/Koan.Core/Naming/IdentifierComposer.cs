using System.Security.Cryptography;
using System.Text;

namespace Koan.Core.Naming;

/// <summary>
/// ARCH-0096: the single engine that composes a physical or logical identifier from an <b>anchor</b> plus an
/// <b>ordered set of policy-rendered particles</b>. Every pillar that builds an identifier (data storage names,
/// cache keys, ...) delegates here, so a cross-cutting axis (partition, tenant) renders identically wherever it
/// appears — the divergence that one hand-rolled composition per pillar produced. Pure, synchronous,
/// deterministic; allocation-free on the no-particle and all-omitted fast paths.
/// </summary>
public static class IdentifierComposer
{
    /// <summary>
    /// Compose <paramref name="anchor"/> with <paramref name="particles"/> under <paramref name="policy"/>.
    /// Particles apply in a total, stable order (by <see cref="Particle.Order"/>, ties broken by axis); a
    /// particle whose formatted token is null/empty is omitted; the result is clamped to the policy's byte limit.
    /// </summary>
    public static string Compose(string anchor, ReadOnlySpan<Particle> particles, in CompositionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        // Fast path: no particles → the anchor itself, unchanged (byte-parity with a bare base name; 0 alloc).
        if (particles.Length == 0)
            return Clamp(anchor, anchor, policy);

        // Single-particle path (the dominant storage-name case): one concat, or the anchor if the token omits.
        if (particles.Length == 1)
        {
            var only = policy.Formatter.Format(particles[0].Value);
            var single = string.IsNullOrEmpty(only) ? anchor : anchor + policy.Separator + only;
            return Clamp(single, anchor, policy);
        }

        // Multi-particle: deterministic order via a stackalloc index buffer (no heap), then format + join.
        Span<int> order = particles.Length <= 16 ? stackalloc int[particles.Length] : new int[particles.Length];
        for (var i = 0; i < particles.Length; i++) order[i] = i;
        StableSort(order, particles);

        var sb = new StringBuilder(anchor.Length + particles.Length * 8);
        sb.Append(anchor);
        var any = false;
        foreach (var oi in order)
        {
            var token = policy.Formatter.Format(particles[oi].Value);
            if (string.IsNullOrEmpty(token)) continue;
            sb.Append(policy.Separator).Append(token);
            any = true;
        }

        return Clamp(any ? sb.ToString() : anchor, anchor, policy);
    }

    private static void StableSort(Span<int> order, ReadOnlySpan<Particle> particles)
    {
        // Insertion sort: stable, allocation-free, ideal for the small N (1-4) particle sets in practice.
        for (var i = 1; i < order.Length; i++)
        {
            var cur = order[i];
            var j = i - 1;
            while (j >= 0 && Compare(particles[order[j]], particles[cur]) > 0)
            {
                order[j + 1] = order[j];
                j--;
            }
            order[j + 1] = cur;
        }
    }

    private static int Compare(in Particle a, in Particle b)
    {
        var c = a.Order.CompareTo(b.Order);
        return c != 0 ? c : string.CompareOrdinal(a.Axis, b.Axis);
    }

    // --- length policy (mirrors Koan.Data.Abstractions.Naming.NamingUtils byte-for-byte, so the data pillar can
    // delegate to this composer without changing any produced name) ---

    private static string Clamp(string identifier, string anchor, in CompositionPolicy policy)
    {
        if (policy.MaxBytes is not { } max || ByteLength(identifier) <= max)
            return identifier;

        const int hashChars = 8;
        var hash = ShortHash(identifier, hashChars);
        var prefix = TrimToBytes(anchor, max - hashChars - policy.Separator.Length); // reserve separator + hash
        return prefix + policy.Separator + hash;
    }

    private static int ByteLength(string value) => Encoding.UTF8.GetByteCount(value ?? "");

    private static string ShortHash(string value, int hexChars = 8)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""));
        var byteCount = Math.Clamp((hexChars + 1) / 2, 1, bytes.Length);
        return Convert.ToHexString(bytes, 0, byteCount).ToLowerInvariant()[..hexChars];
    }

    private static string TrimToBytes(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || ByteLength(value) <= maxBytes) return value ?? "";
        var take = Math.Min(value.Length, maxBytes);
        while (take > 0 && ByteLength(value[..take]) > maxBytes) take--;
        return value[..take];
    }
}
