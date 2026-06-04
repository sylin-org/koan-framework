using System.Diagnostics;

namespace Koan.Jobs.Tests.Support;

/// <summary>Real-time spin for the few concurrency tests (cancel/timeout of a job mid-flight) where we need to
/// observe a background drain reach a known point before acting. Uses wall-clock, independent of the fake clock.</summary>
public static class Wait
{
    public static async Task Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(5);
        }
    }
}
