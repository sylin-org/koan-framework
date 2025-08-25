namespace Sora.Messaging;

// Shared retry sequence/bucket helpers for adapters and core components
public static class RetryMath
{
    // Full attempt-by-attempt delay sequence (attempts 2..MaxAttempts)
    public static IEnumerable<int> Sequence(RetryOptions retry)
    {
        var maxA = Math.Max(2, retry.MaxAttempts);
        var first = Math.Max(1, retry.FirstDelaySeconds);
        var cap = Math.Max(first, retry.MaxDelaySeconds <= 0 ? first * 16 : retry.MaxDelaySeconds);
        var cur = first;
        for (int attempt = 2; attempt <= maxA; attempt++)
        {
            yield return cur;
            if (string.Equals(retry.Backoff, "fixed", StringComparison.OrdinalIgnoreCase))
                cur = first;
            else
                cur = Math.Min(cap, cur * 2);
        }
    }

    // Distinct bucket set for infra provisioning (e.g., TTL queues)
    public static IEnumerable<int> Buckets(RetryOptions retry)
        => Sequence(retry).Distinct().OrderBy(x => x);

    // Choose the nearest bucket >= requestedSeconds; fallback to last bucket or requestedSeconds
    public static int ChooseBucket(int requestedSeconds, RetryOptions retry)
    {
        var seq = Sequence(retry).ToArray();
        if (seq.Length == 0) return requestedSeconds;
        foreach (var s in seq)
            if (s >= requestedSeconds) return s;
        return seq[^1];
    }
}
