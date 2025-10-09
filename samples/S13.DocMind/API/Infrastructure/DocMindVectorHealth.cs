using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace S13.DocMind.Infrastructure;

public sealed class DocMindVectorHealth
{
    private readonly object _sync = new();
    private bool _adapterAvailable;
    private bool _fallbackActive;
    private DateTimeOffset? _lastAuditAt;
    private DateTimeOffset? _lastSearchAt;
    private DateTimeOffset? _lastGenerationAt;
    private string? _lastAuditError;
    private IReadOnlyList<string> _missingProfiles = Array.Empty<string>();
    private double? _lastSearchLatencyMs;
    private double? _lastGenerationDurationMs;
    private string? _lastAdapterModel;
    private DocMindVectorReadinessSnapshot _snapshot = new(false, true, null, "Vector readiness not audited", Array.Empty<string>(), null, null, null, null, null);
    private static DocMindVectorReadinessSnapshot _latestSnapshot = new(false, true, null, "Vector readiness not audited", Array.Empty<string>(), null, null, null, null, null);

    public void RecordAudit(bool adapterAvailable, IEnumerable<string> missingProfiles, string? error)
    {
        lock (_sync)
        {
            _adapterAvailable = adapterAvailable;
            _missingProfiles = missingProfiles?.ToArray() ?? Array.Empty<string>();
            _lastAuditError = error;
            _lastAuditAt = DateTimeOffset.UtcNow;
            if (!adapterAvailable)
            {
                _fallbackActive = true;
            }
            UpdateSnapshotLocked();
        }
    }

    public void RecordSearch(TimeSpan latency, bool fallback)
    {
        lock (_sync)
        {
            _lastSearchLatencyMs = latency == TimeSpan.Zero ? null : latency.TotalMilliseconds;
            _lastSearchAt = DateTimeOffset.UtcNow;
            _fallbackActive = fallback || !_adapterAvailable;
            UpdateSnapshotLocked();
        }
    }

    public void RecordGeneration(TimeSpan duration, string? model, bool succeeded)
    {
        lock (_sync)
        {
            _lastGenerationDurationMs = duration == TimeSpan.Zero ? null : duration.TotalMilliseconds;
            _lastGenerationAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(model))
            {
                _lastAdapterModel = model;
            }
            if (!succeeded)
            {
                _fallbackActive = true;
            }
            UpdateSnapshotLocked();
        }
    }

    public DocMindVectorReadinessSnapshot Snapshot()
    {
        lock (_sync)
        {
            return UpdateSnapshotLocked();
        }
    }

    public static DocMindVectorReadinessSnapshot LatestSnapshot
        => Volatile.Read(ref _latestSnapshot);

    private DocMindVectorReadinessSnapshot UpdateSnapshotLocked()
    {
        _snapshot = new DocMindVectorReadinessSnapshot(
            _adapterAvailable,
            _fallbackActive,
            _lastAuditAt,
            _lastAuditError,
            _missingProfiles,
            _lastSearchLatencyMs,
            _lastGenerationDurationMs,
            _lastSearchAt,
            _lastGenerationAt,
            _lastAdapterModel);

        Volatile.Write(ref _latestSnapshot, _snapshot);
        return _snapshot;
    }
}

public sealed record DocMindVectorReadinessSnapshot(
    bool AdapterAvailable,
    bool FallbackActive,
    DateTimeOffset? LastAuditAt,
    string? LastAuditError,
    IReadOnlyList<string> MissingProfiles,
    double? LastSearchLatencyMs,
    double? LastGenerationDurationMs,
    DateTimeOffset? LastSearchAt,
    DateTimeOffset? LastGenerationAt,
    string? LastAdapterModel);
