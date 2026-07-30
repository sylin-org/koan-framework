using System.Collections.Concurrent;

namespace Koan.Data.Abstractions;

/// <summary>One bounded, fixed-shape, provider-neutral buffered result.</summary>
public sealed class RecordSet
{
    private readonly ConcurrentDictionary<Type, object> _projectionPlans = new();

    public RecordSet(
        IReadOnlyList<DataField> fields,
        IReadOnlyList<DataRecord> records,
        RecordSetCompletion completion,
        RecordSetExecution execution)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(execution);
        execution.EffectiveLimits.Validate();

        var shape = fields.ToArray();
        for (var i = 0; i < shape.Length; i++)
            if (shape[i].Ordinal != i)
                throw new ArgumentException("RecordSet field ordinals must be contiguous and match field order.", nameof(fields));
        foreach (var record in records)
        {
            if (record.FieldCount != shape.Length)
                throw new ArgumentException("Every record must use the shared RecordSet field cardinality.", nameof(records));
            for (var i = 0; i < shape.Length; i++)
                if (record.Field(i) != shape[i])
                    throw new ArgumentException("Every record must use the exact shared RecordSet field shape.", nameof(records));
        }

        Fields = Array.AsReadOnly(shape);
        Records = Array.AsReadOnly(records.ToArray());
        Completion = completion;
        Execution = execution;
    }

    public IReadOnlyList<DataField> Fields { get; }
    public IReadOnlyList<DataRecord> Records { get; }
    public RecordSetCompletion Completion { get; }
    public bool IsComplete => Completion == RecordSetCompletion.Complete;
    public RecordSetExecution Execution { get; }

    public IReadOnlyList<T> Project<T>()
    {
        var plan = (Func<DataRecord, T>)_projectionPlans.GetOrAdd(
            typeof(T),
            _ => RecordProjector.Compile<T>(Fields));
        var projected = new T[Records.Count];
        for (var i = 0; i < Records.Count; i++) projected[i] = plan(Records[i]);
        return projected;
    }
}
