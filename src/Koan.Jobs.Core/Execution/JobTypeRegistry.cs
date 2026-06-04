using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Jobs.Model;
using Koan.Jobs.Queue;
using Microsoft.Extensions.Logging;

namespace Koan.Jobs.Execution;

/// <summary>
/// Discovers concrete job types (every <see cref="IKoanJob"/> implementor) and drives the
/// cross-cutting operations that, without a unified job set, become registry fan-out (JOBS-0003):
/// boot recovery and archival retention. Also resolves a stored <see cref="JobRef.TypeName"/> back
/// to its CLR type so dependency checks can load the referenced job.
/// </summary>
internal sealed class JobTypeRegistry
{
    private readonly List<Type> _types;
    private readonly Dictionary<string, Type> _byName;

    public JobTypeRegistry()
    {
        _types = Discover();
        _byName = new(StringComparer.Ordinal);
        foreach (var t in _types) _byName[t.FullName ?? t.Name] = t;
    }

    public IReadOnlyList<Type> Types => _types;

    public Type? Resolve(string typeName)
        => _byName.TryGetValue(typeName, out var t) ? t : null;

    private static List<Type> Discover()
    {
        var result = new List<Type>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t is null || t.IsAbstract || t.IsGenericTypeDefinition || !t.IsClass) continue;
                if (typeof(IKoanJob).IsAssignableFrom(t)) result.Add(t);
            }
        }
        return result;
    }

    /// <summary>Re-enqueue every persisted non-terminal job across all types (boot recovery).</summary>
    public async Task RecoverAll(IJobQueue queue, ILogger logger, CancellationToken cancellationToken)
    {
        foreach (var t in _types)
        {
            var method = typeof(JobTypeOps<>).MakeGenericType(t).GetMethod("RecoverInto")!;
            await (Task)method.Invoke(null, new object?[] { queue, logger, cancellationToken })!;
        }
    }

    /// <summary>Remove terminal jobs older than the cutoff across all types (archival).</summary>
    public async Task<int> SweepArchival(DateTimeOffset cutoff, int batchPerType, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var t in _types)
        {
            var method = typeof(JobTypeOps<>).MakeGenericType(t).GetMethod("SweepArchival")!;
            total += await (Task<int>)method.Invoke(null, new object?[] { cutoff, batchPerType, cancellationToken })!;
        }
        return total;
    }

    /// <summary>Resolve the status of a referenced job, or null if the type is unknown or the row is gone.</summary>
    public async Task<JobStatus?> StatusOf(JobRef reference, CancellationToken cancellationToken)
    {
        var type = Resolve(reference.TypeName);
        if (type is null) return null;
        var method = typeof(JobTypeOps<>).MakeGenericType(type).GetMethod("StatusOf")!;
        return await (Task<JobStatus?>)method.Invoke(null, new object?[] { reference.Id, cancellationToken })!;
    }

    /// <summary>Revert non-terminal Running jobs whose lease has lapsed to <see cref="JobStatus.Queued"/>
    /// across every registered type, re-enqueuing each for redispatch. Returns the number of rows
    /// recovered. Used by <c>JobOrphanReaper</c> for runtime orphan recovery (the worker died after the
    /// last heartbeat but before completing the body).</summary>
    public async Task<int> ReapOrphansAll(IJobQueue queue, DateTimeOffset now, ILogger logger, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var t in _types)
        {
            var method = typeof(JobTypeOps<>).MakeGenericType(t).GetMethod("ReapOrphansInto")!;
            total += await (Task<int>)method.Invoke(null, new object?[] { queue, now, logger, cancellationToken })!;
        }
        return total;
    }
}

/// <summary>Per-type generic operations invoked by <see cref="JobTypeRegistry"/> via reflection.</summary>
internal static class JobTypeOps<T> where T : Job<T>, new()
{
    public static async Task RecoverInto(IJobQueue queue, ILogger logger, CancellationToken cancellationToken)
    {
        var pending = await Job<T>.Query(
            j => j.Status == JobStatus.Queued || j.Status == JobStatus.Running || j.Status == JobStatus.Blocked,
            cancellationToken);
        if (pending.Count == 0) return;

        foreach (var job in pending)
        {
            // A Running row means the host died mid-execution; re-queue it (at-least-once + idempotent).
            await queue.Enqueue(new JobQueueItem(job.Id, typeof(T), job.LaneNameInternal), cancellationToken);
        }
        logger.LogInformation("Recovered {Count} non-terminal {Type} job(s) on startup.", pending.Count, typeof(T).Name);
    }

    public static async Task<int> SweepArchival(DateTimeOffset cutoff, int batch, CancellationToken cancellationToken)
    {
        var expired = await Job<T>.Query(
            j => (j.Status == JobStatus.Completed || j.Status == JobStatus.Failed || j.Status == JobStatus.Cancelled)
                 && j.CompletedAt != null && j.CompletedAt < cutoff,
            cancellationToken);
        var ids = expired.Take(Math.Max(1, batch)).Select(j => j.Id).ToList();
        if (ids.Count == 0) return 0;
        await Job<T>.Remove(ids, cancellationToken);
        return ids.Count;
    }

    public static async Task<JobStatus?> StatusOf(string id, CancellationToken cancellationToken)
    {
        var job = await Job<T>.Get(id, cancellationToken);
        return job?.Status;
    }

    /// <summary>Find Running jobs whose lease has lapsed (or was never stamped) and revert them to
    /// Queued so the worker can pick them back up. Clears mid-run progress so the retry starts clean.
    /// Idempotent: safe to call concurrently across hosts because both the lease check and the SaveSelf
    /// only act on rows whose lease is still considered stale at the moment of save.</summary>
    public static async Task<int> ReapOrphansInto(IJobQueue queue, DateTimeOffset now, ILogger logger, CancellationToken cancellationToken)
    {
        var orphans = await Job<T>.Query(
            j => j.Status == JobStatus.Running && (j.LeasedUntil == null || j.LeasedUntil < now),
            cancellationToken);
        if (orphans.Count == 0) return 0;

        var recovered = 0;
        foreach (var job in orphans)
        {
            // Re-check under the loaded row: another reaper may have already flipped it.
            if (job.Status != JobStatus.Running) continue;
            if (job.LeasedUntil is not null && job.LeasedUntil >= now) continue;

            job.Status = JobStatus.Queued;
            job.Attempt++;
            job.StartedAt = null;
            job.LeasedUntil = null;
            job.Progress = 0;
            job.CurrentStep = null;
            job.ProgressMessage = "Recovered after lease lapsed.";
            job.QueuedAt = now;
            await job.SaveSelf(cancellationToken);
            await queue.Enqueue(new JobQueueItem(job.Id, typeof(T), job.LaneNameInternal), cancellationToken);
            recovered++;
        }

        if (recovered > 0)
            logger.LogInformation("Reaped {Count} orphan {Type} job(s) past their lease.", recovered, typeof(T).Name);
        return recovered;
    }
}
