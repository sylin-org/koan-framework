using System.Collections.Concurrent;
using Koan.Data.Core.Model;

namespace Koan.Jobs;

/// <summary>The lifecycle moments a job passes through. Past participles — they observe, never intervene.</summary>
public enum JobEventKind
{
    Claimed,
    Completed,
    Failed,
    DeadLettered,
    Rescheduled,
    Cancelled,
    Stalled,
    Abandoned,
    Enqueued,
}

/// <summary>
/// One lifecycle moment for one job. <see cref="Model"/> is the typed work item when the pipeline
/// has it loaded; handlers observe — they never participate in settlement, and a throwing handler
/// is swallowed by design.
/// </summary>
public sealed class JobEvent<TModel>
    where TModel : Entity<TModel>, IKoanJob<TModel>
{
    public required JobEventKind Kind { get; init; }
    public required JobRecord Record { get; init; }
    public TModel? Model { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Per-closed-type observer registry. Pure projection: fired after the durable write, never on it.
/// </summary>
internal static class JobEventRegistry
{
    private readonly record struct Observer(JobEventKind Kind, Action<object?, JobRecord, string?> Invoke);

    private static readonly ConcurrentDictionary<string, List<Observer>> Observers = new(StringComparer.Ordinal);

    public static void Add<TModel>(JobEventKind kind, Action<JobEvent<TModel>> handler)
        where TModel : Entity<TModel>, IKoanJob<TModel>
    {
        ArgumentNullException.ThrowIfNull(handler);
        var list = Observers.GetOrAdd(typeof(TModel).FullName!, static _ => new List<Observer>());
        lock (list)
        {
            list.Add(new Observer(kind, (model, record, error) =>
                handler(new JobEvent<TModel> { Kind = kind, Record = record, Model = model as TModel, Error = error })));
        }
    }

    public static void Publish(string workType, JobEventKind kind, object? model, JobRecord record, string? error = null)
    {
        if (!Observers.TryGetValue(workType, out var list)) return;
        Observer[] snapshot;
        lock (list)
        {
            snapshot = [.. list];
        }

        foreach (var observer in snapshot)
        {
            if (observer.Kind != kind) continue;
            try
            {
                observer.Invoke(model, record.Clone(), error);
            }
            catch
            {
                // Observers are projections: a throwing handler can never affect settlement.
            }
        }
    }

    public static void Reset<TModel>()
        where TModel : Entity<TModel>, IKoanJob<TModel>
        => Observers.TryRemove(typeof(TModel).FullName!, out _);
}
