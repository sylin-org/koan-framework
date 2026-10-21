using System;

namespace Koan.Data.Core.Transfers;

/// <summary>
/// One side of a transfer as the caller declared it (DATA-0153). An axis left <see langword="null"/> inherits the
/// ambient <see cref="EntityContext"/>; an empty or blank axis names its base value (the default source, no adapter
/// override, the base partition). Naming a source or an adapter replaces the ambient routing as a whole, so the other
/// of the two is not inherited: <c>To(adapter: "mongo")</c> inside <c>EntityContext.Source("Preview")</c> reaches the
/// adapter's default store, and <c>From(source: "", partition: "")</c> is the base set under any ambient scope.
/// </summary>
public readonly struct TransferContextOptions
{
    /// <summary>The source to use: <see langword="null"/> inherits, <see cref="string.Empty"/> is the default source.</summary>
    public string? Source { get; }

    /// <summary>The adapter to use: <see langword="null"/> inherits, <see cref="string.Empty"/> clears an adapter override.</summary>
    public string? Adapter { get; }

    /// <summary>The partition to use: <see langword="null"/> inherits, <see cref="string.Empty"/> is the base partition.</summary>
    public string? Partition { get; }

    public TransferContextOptions(string? source, string? adapter, string? partition)
    {
        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(adapter))
            throw new InvalidOperationException("Cannot specify both 'source' and 'adapter'. Sources define their own adapters.");

        Source = Declared(source);
        Adapter = Declared(adapter);
        Partition = Declared(partition);
    }

    /// <summary>
    /// Enters this side's routing. A named source clears an ambient adapter and a named adapter clears an ambient
    /// source, because a source and an adapter are two ways to name one route (DATA-0153).
    /// </summary>
    public IDisposable Apply()
        => EntityContext.With(
            source: Source ?? (string.IsNullOrEmpty(Adapter) ? null : string.Empty),
            adapter: Adapter ?? (Source is null ? null : string.Empty),
            partition: Partition);

    public TransferContextSnapshot Snapshot()
        => new(Source, Adapter, Partition);

    private static string? Declared(string? axis)
        => axis is null ? null : string.IsNullOrWhiteSpace(axis) ? string.Empty : axis;
}

public sealed record TransferContextSnapshot(string? Source, string? Adapter, string? Partition)
{
    public static TransferContextSnapshot Empty { get; } = new(null, null, null);
}
