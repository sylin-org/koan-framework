using Koan.Data.Abstractions.Failures;
using Microsoft.Extensions.Options;

namespace Koan.Data.Core.Diagnostics;

/// <summary>Bounded host-owned restricted evidence store exposed to adapters only through its write-only seam.</summary>
internal sealed class DataNativeEvidenceStore : IDataNativeEvidenceSink
{
    private readonly object _gate = new();
    private readonly int _limit;
    private readonly Dictionary<string, DataNativeEvidenceRecord> _records = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public DataNativeEvidenceStore(IOptions<DataRuntimeOptions> options)
    {
        _limit = options.Value.NativeEvidenceEntries;
        if (_limit <= 0) throw new ArgumentOutOfRangeException(nameof(options), "NativeEvidenceEntries must be positive.");
    }

    public string Record(Exception nativeFailure, DataNativeEvidenceContext context, string? nativeCode = null)
    {
        ArgumentNullException.ThrowIfNull(nativeFailure);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.OperationCode);
        var reference = $"EVD-{Guid.NewGuid():N}";
        var record = new DataNativeEvidenceRecord(
            reference,
            context.Provider.Trim(),
            nativeFailure.GetType().FullName ?? nativeFailure.GetType().Name,
            string.IsNullOrWhiteSpace(nativeCode) ? null : nativeCode.Trim(),
            context.Target,
            context.OperationCode.Trim(),
            string.IsNullOrWhiteSpace(context.CorrelationId) ? null : context.CorrelationId.Trim(),
            DateTimeOffset.UtcNow);
        lock (_gate)
        {
            while (_records.Count >= _limit && _order.TryDequeue(out var expired)) _records.Remove(expired);
            _records.Add(reference, record);
            _order.Enqueue(reference);
        }
        return reference;
    }

    internal bool TryGet(string reference, out DataNativeEvidenceRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        lock (_gate) return _records.TryGetValue(reference, out record!);
    }

    internal IReadOnlyList<DataNativeEvidenceRecord> Snapshot()
    {
        lock (_gate) return _order.Where(_records.ContainsKey).Select(reference => _records[reference]).ToArray();
    }
}
