using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Koan.Jobs.Model;

namespace Koan.Jobs.Support;

internal sealed class JobIndexCache
{
    private readonly ConcurrentDictionary<string, JobIndexEntry> _entries = new();

    public void Set(JobIndexEntry entry) => _entries[entry.JobId] = entry;

    public bool TryGet(string jobId, [NotNullWhen(true)] out JobIndexEntry? entry) => _entries.TryGetValue(jobId, out entry);

    public void Remove(string jobId) => _entries.TryRemove(jobId, out _);
}

/// <summary>
/// Tracks job storage metadata and ephemeral runtime state.
/// This is in-memory only and lost on application restart.
/// </summary>
internal sealed class JobIndexEntry
{
    public string JobId { get; }
    public JobStorageMode StorageMode { get; }
    public string? Source { get; }
    public string? Partition { get; }
    public bool AuditEnabled { get; }
    public Type JobType { get; }

    /// <summary>
    /// Ephemeral cancellation flag. Lost on app restart.
    /// </summary>
    public bool CancellationRequested { get; set; }

    public JobIndexEntry(
        string jobId,
        JobStorageMode storageMode,
        string? source,
        string? partition,
        bool auditEnabled,
        Type jobType)
    {
        JobId = jobId;
        StorageMode = storageMode;
        Source = source;
        Partition = partition;
        AuditEnabled = auditEnabled;
        JobType = jobType;
    }
}
