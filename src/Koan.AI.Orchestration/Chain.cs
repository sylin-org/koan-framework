namespace Koan.AI.Orchestration;

/// <summary>
/// Entry point for building typed, immutable AI chains.
///
/// <code>
/// var result = await Chain.Create()
///     .System("You are a helpful assistant.")
///     .Chat("Summarize: {content}")
///     .Parse&lt;Summary&gt;()
///     .Run(new { content = article });
/// </code>
/// </summary>
public static class Chain
{
    /// <summary>Create a new chain builder.</summary>
    public static ChainBuilder Create() => new();
}
