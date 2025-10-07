using System.Threading;

namespace Koan.Mcp.CodeMode.Sdk;

/// <summary>
/// Internal metrics tracking for code execution.
/// </summary>
internal sealed class MetricsDomain
{
    private int _totalCalls;

    /// <summary>
    /// Increment the total entity operation call count.
    /// </summary>
    public void IncrementCalls()
    {
        Interlocked.Increment(ref _totalCalls);
    }

    /// <summary>
    /// Get the total number of entity operation calls made.
    /// </summary>
    public int GetTotalCalls() => _totalCalls;
}
