using System;
using System.Collections.Concurrent;
using Koan.Jobs.Model;

namespace Koan.Jobs.Support;

internal sealed class JobIndexCache
{
    private readonly ConcurrentDictionary<string, JobIndexEntry> _entries = new();

    public void Set(JobIndexEntry entry) => _entries[entry.JobId] = entry;

    public bool TryGet(string jobId, out JobIndexEntry entry) => _entries.TryGetValue(jobId, out entry);

    public void Remove(string jobId) => _entries.TryRemove(jobId, out _);
}

internal readonly record struct JobIndexEntry(
    string JobId,
    JobStorageMode StorageMode,
    string? Source,
    string? Partition,
    Type JobType);
