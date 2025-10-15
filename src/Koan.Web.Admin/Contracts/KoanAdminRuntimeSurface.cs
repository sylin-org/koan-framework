using System;
using System.Collections.Generic;

namespace Koan.Web.Admin.Contracts;

public sealed record KoanAdminRuntimeSurface(
    DateTimeOffset CapturedAtUtc,
    bool Sanitized,
    bool Locked,
    string? LockReason,
    IReadOnlyList<string> SanitizedFields,
    KoanAdminRuntimeProcess Process,
    KoanAdminRuntimeMemory Memory,
    KoanAdminRuntimeGc GarbageCollector,
    KoanAdminRuntimeThreadPool ThreadPool,
    KoanAdminRuntimeMachine Machine)
{
    public static KoanAdminRuntimeSurface Empty { get; } = new(
        DateTimeOffset.MinValue,
        true,
        true,
        "Runtime snapshot unavailable.",
        Array.Empty<string>(),
        new KoanAdminRuntimeProcess(
            0,
            "unknown",
            null,
            null,
            null,
            null,
            DateTimeOffset.MinValue,
            0,
            0,
            0,
            0,
            0),
        new KoanAdminRuntimeMemory(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        new KoanAdminRuntimeGc(0, false, "Unknown", Array.Empty<int>(), 0, 0, 0),
        new KoanAdminRuntimeThreadPool(0, 0, 0, 0, 0, 0, 0, 0, 0),
        new KoanAdminRuntimeMachine(null, null, string.Empty, string.Empty, string.Empty, string.Empty, 0, false, false));
}

public sealed record KoanAdminRuntimeProcess(
    int ProcessId,
    string Name,
    string? UserName,
    string? CommandLine,
    string? ExecutablePath,
    string? WorkingDirectory,
    DateTimeOffset StartTimeUtc,
    double UptimeSeconds,
    double TotalProcessorTimeSeconds,
    double CpuUtilizationPercent,
    int ThreadCount,
    int HandleCount);

public sealed record KoanAdminRuntimeMemory(
    long WorkingSetBytes,
    long PeakWorkingSetBytes,
    long PrivateBytes,
    long VirtualBytes,
    long PagedBytes,
    long PagedSystemBytes,
    long NonPagedSystemBytes,
    long GcHeapSizeBytes,
    long GcFragmentedBytes,
    long GcTotalCommittedBytes,
    long ManagedHeapBytes,
    long ManagedAllocatedBytes);

public sealed record KoanAdminRuntimeGc(
    int MaxGeneration,
    bool IsServerGc,
    string LatencyMode,
    IReadOnlyList<int> CollectionCounts,
    long HighMemoryLoadThresholdBytes,
    long MemoryLoadBytes,
    double MemoryLoadPercent);

public sealed record KoanAdminRuntimeThreadPool(
    int ThreadCount,
    int AvailableWorkerThreads,
    int MinWorkerThreads,
    int MaxWorkerThreads,
    int AvailableCompletionPortThreads,
    int MinCompletionPortThreads,
    int MaxCompletionPortThreads,
    long CompletedWorkItemCount,
    long PendingWorkItemCount);

public sealed record KoanAdminRuntimeMachine(
    string? MachineName,
    string? DomainName,
    string FrameworkDescription,
    string OSDescription,
    string ProcessArchitecture,
    string OSArchitecture,
    int ProcessorCount,
    bool Is64BitProcess,
    bool Is64BitOperatingSystem);
