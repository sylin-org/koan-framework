using System.Collections.Concurrent;
using Koan.Data.Analytics.Infrastructure;

namespace Koan.Data.Analytics;

/// <summary>
/// The request-a-recipe loop's memory: every ask for a question that does not exist is refused loudly
/// *and recorded*, because the gap between the catalog and what people actually ask is a product signal,
/// not noise. Coverage is managed; the log is how.
/// </summary>
public sealed class AnalyticsRecipeGapLog
{
    private readonly ConcurrentQueue<(string Name, DateTimeOffset At)> _gaps = new();
    private int _count;

    public void Record(string name)
    {
        _gaps.Enqueue((name, DateTimeOffset.UtcNow));
        Interlocked.Increment(ref _count);
        while (_gaps.Count > Constants.GapLogCapacity && _gaps.TryDequeue(out _)) { }
    }

    public int TotalCount => Interlocked.CompareExchange(ref _count, 0, 0);

    public IReadOnlyList<(string Name, DateTimeOffset At)> Recent(int take = 20)
    {
        if (take <= 0) return [];
        return _gaps.TakeLast(take).ToArray();
    }
}
