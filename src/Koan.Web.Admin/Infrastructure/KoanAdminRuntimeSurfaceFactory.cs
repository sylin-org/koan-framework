using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using Koan.Core;
using Koan.Web.Admin.Contracts;

namespace Koan.Web.Admin.Infrastructure;

internal static class KoanAdminRuntimeSurfaceFactory
{
    public static KoanAdminRuntimeSurface Capture(bool sanitized, bool locked, string? lockReason)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        using var process = Process.GetCurrentProcess();

        List<string>? sanitizedFields = sanitized ? new List<string>() : null;

        var startTimeUtc = KoanEnv.ProcessStart;
        var uptime = capturedAt - startTimeUtc;
        if (uptime < TimeSpan.Zero)
        {
            uptime = TimeSpan.Zero;
        }

        var totalProcessorSeconds = SafeGet(() => process.TotalProcessorTime.TotalSeconds, 0d);
        var cpuPercent = ComputeCpuPercent(totalProcessorSeconds, uptime, Environment.ProcessorCount);

        var processSurface = new KoanAdminRuntimeProcess(
            process.Id,
            SafeGet(() => process.ProcessName) ?? "unknown",
            sanitized ? null : SafeGet(() => Environment.UserName),
            sanitized ? null : SafeGet(() => Environment.CommandLine),
            sanitized ? null : SafeGet(() => process.MainModule?.FileName),
            sanitized ? null : SafeGet(() => Environment.CurrentDirectory),
            startTimeUtc,
            Math.Max(0d, uptime.TotalSeconds),
            totalProcessorSeconds,
            cpuPercent,
            SafeGet(() => process.Threads.Count, 0),
            SafeGet(() => process.HandleCount, 0));

        if (sanitized)
        {
            AddSanitizedField(sanitizedFields, "Process.UserName");
            AddSanitizedField(sanitizedFields, "Process.CommandLine");
            AddSanitizedField(sanitizedFields, "Process.ExecutablePath");
            AddSanitizedField(sanitizedFields, "Process.WorkingDirectory");
        }

        var memory = BuildMemory(process);
        var gc = BuildGc();
        var threadPool = BuildThreadPool();
        var machine = BuildMachine(sanitized, sanitizedFields);

        var sanitizedFieldView = sanitizedFields is { Count: > 0 }
            ? sanitizedFields.Distinct(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

        return new KoanAdminRuntimeSurface(
            capturedAt,
            sanitized,
            locked,
            lockReason,
            sanitizedFieldView,
            processSurface,
            memory,
            gc,
            threadPool,
            machine);
    }

    private static KoanAdminRuntimeMemory BuildMemory(Process process)
    {
        var workingSet = SafeGet(() => process.WorkingSet64, 0L);
        var peakWorkingSet = SafeGet(() => process.PeakWorkingSet64, 0L);
        var privateBytes = SafeGet(() => process.PrivateMemorySize64, 0L);
        var virtualBytes = SafeGet(() => process.VirtualMemorySize64, 0L);
        var pagedBytes = SafeGet(() => process.PagedMemorySize64, 0L);
        var pagedSystemBytes = SafeGet(() => process.PagedSystemMemorySize64, 0L);
        var nonPagedSystemBytes = SafeGet(() => process.NonpagedSystemMemorySize64, 0L);

        var managedHeapBytes = SafeGet(() => GC.GetTotalMemory(forceFullCollection: false), 0L);
        var managedAllocatedBytes = SafeGet(() => GC.GetTotalAllocatedBytes(), 0L);
        var gcInfo = GC.GetGCMemoryInfo();

        return new KoanAdminRuntimeMemory(
            workingSet,
            peakWorkingSet,
            privateBytes,
            virtualBytes,
            pagedBytes,
            pagedSystemBytes,
            nonPagedSystemBytes,
            gcInfo.HeapSizeBytes,
            gcInfo.FragmentedBytes,
            gcInfo.TotalCommittedBytes,
            managedHeapBytes,
            managedAllocatedBytes);
    }

    private static KoanAdminRuntimeGc BuildGc()
    {
        var generationCount = GC.MaxGeneration;
        var collections = Enumerable.Range(0, generationCount + 1)
            .Select(static generation => SafeGet(() => GC.CollectionCount(generation), 0))
            .ToArray();

        var gcInfo = GC.GetGCMemoryInfo();
        var highThreshold = gcInfo.HighMemoryLoadThresholdBytes;
        var memoryLoad = gcInfo.MemoryLoadBytes;
        var memoryLoadPercent = highThreshold > 0
            ? Math.Round((double)memoryLoad / highThreshold * 100, 2, MidpointRounding.AwayFromZero)
            : 0d;

        return new KoanAdminRuntimeGc(
            generationCount,
            GCSettings.IsServerGC,
            GCSettings.LatencyMode.ToString(),
            collections,
            highThreshold,
            memoryLoad,
            memoryLoadPercent);
    }

    private static KoanAdminRuntimeThreadPool BuildThreadPool()
    {
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
        ThreadPool.GetMaxThreads(out var maxWorkers, out var maxIo);

        var completed = SafeGet(() => ThreadPool.CompletedWorkItemCount, 0L);
        var pending = SafeGet(() => ThreadPool.PendingWorkItemCount, 0L);

        return new KoanAdminRuntimeThreadPool(
            ThreadPool.ThreadCount,
            availableWorkers,
            minWorkers,
            maxWorkers,
            availableIo,
            minIo,
            maxIo,
            completed,
            pending);
    }

    private static KoanAdminRuntimeMachine BuildMachine(bool sanitized, List<string>? sanitizedFields)
    {
        string? machineName = null;
        string? domainName = null;

        if (!sanitized)
        {
            machineName = SafeGet(static () => Environment.MachineName);
            domainName = SafeGet(static () => Environment.UserDomainName);
        }
        else
        {
            AddSanitizedField(sanitizedFields, "Machine.MachineName");
            AddSanitizedField(sanitizedFields, "Machine.DomainName");
        }

        return new KoanAdminRuntimeMachine(
            machineName,
            domainName,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.ProcessorCount,
            Environment.Is64BitProcess,
            Environment.Is64BitOperatingSystem);
    }

    private static double ComputeCpuPercent(double totalProcessorSeconds, TimeSpan uptime, int processorCount)
    {
        if (uptime.TotalSeconds <= 0 || processorCount <= 0)
        {
            return 0;
        }

        var normalized = totalProcessorSeconds / uptime.TotalSeconds / processorCount;
        var percent = normalized * 100;
        return Math.Round(percent, 2, MidpointRounding.AwayFromZero);
    }

    private static T SafeGet<T>(Func<T> accessor, T fallback = default!)
    {
        try
        {
            return accessor();
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddSanitizedField(List<string>? list, string field)
    {
        if (list is null)
        {
            return;
        }

        if (!list.Any(entry => string.Equals(entry, field, StringComparison.Ordinal)))
        {
            list.Add(field);
        }
    }
}
