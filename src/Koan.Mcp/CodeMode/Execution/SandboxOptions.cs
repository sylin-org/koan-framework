namespace Koan.Mcp.CodeMode.Execution;

/// <summary>
/// Configuration for code execution sandbox limits.
/// </summary>
public sealed class SandboxOptions
{
    /// <summary>
    /// Maximum execution time in milliseconds.
    /// Default: 2000ms (2 seconds).
    /// </summary>
    public int CpuMilliseconds { get; set; } = 2000;

    /// <summary>
    /// Maximum memory usage in megabytes.
    /// Default: 64 MB.
    /// </summary>
    public int MemoryMegabytes { get; set; } = 64;

    /// <summary>
    /// Maximum recursion depth.
    /// Default: 100.
    /// </summary>
    public int MaxRecursionDepth { get; set; } = 100;

    /// <summary>
    /// Maximum code length in characters.
    /// Default: 50000 (50k characters ~= 12k tokens).
    /// </summary>
    public int MaxCodeLength { get; set; } = 50000;
}
