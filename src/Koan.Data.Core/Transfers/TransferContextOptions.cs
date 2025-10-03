using System;

namespace Koan.Data.Core.Transfers;

public readonly struct TransferContextOptions
{
    public string? Source { get; }
    public string? Adapter { get; }
    public string? Partition { get; }

    public TransferContextOptions(string? source, string? adapter, string? partition)
    {
        if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(adapter))
            throw new InvalidOperationException("Cannot specify both 'source' and 'adapter'. Sources define their own adapters.");

        Source = string.IsNullOrWhiteSpace(source) ? null : source;
        Adapter = string.IsNullOrWhiteSpace(adapter) ? null : adapter;
        Partition = string.IsNullOrWhiteSpace(partition) ? null : partition;
    }

    public IDisposable Apply()
        => EntityContext.With(Source, Adapter, Partition);

    public TransferContextSnapshot Snapshot()
        => new(Source, Adapter, Partition);
}

public sealed record TransferContextSnapshot(string? Source, string? Adapter, string? Partition)
{
    public static TransferContextSnapshot Empty { get; } = new(null, null, null);
}



