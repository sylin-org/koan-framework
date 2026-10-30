using System.Collections.Concurrent;
using Koan.Core.Infrastructure;

namespace Koan.Core.Diagnostics;

/// <summary>
/// The host's runtime facts. A hot path records the same decision over and over (every fallback query, every stream),
/// so recording a fact that is already held unchanged takes no lock and writes nothing; a changed fact is written and
/// marks the envelope stale. The ordered envelope is built once per change, when it is next read, never per record.
/// </summary>
internal sealed class KoanRuntimeFactStore : IKoanRuntimeFacts, IKoanRuntimeFactRecorder
{
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, KoanFact> _facts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collectedFactIds = new(StringComparer.Ordinal);
    private readonly string _sessionId = Guid.CreateVersion7().ToString("n");
    // Bumped by every write, and the envelope's sequence: it counts changes, and an envelope is current while its own
    // Sequence equals this version. The envelope carries the version it was built at, so the check cannot pair one
    // envelope with another build's version.
    private long _version;
    private bool _complete;
    private KoanFactEnvelope? _current;

    public KoanFactEnvelope Current
    {
        get
        {
            var current = Volatile.Read(ref _current);
            if (current is not null && current.Sequence == Volatile.Read(ref _version)) return current;
            lock (_gate)
            {
                return _current is not null && _current.Sequence == _version ? _current : Build();
            }
        }
    }

    internal KoanFactEnvelope Replace(IEnumerable<KoanFact> facts, bool complete)
    {
        ArgumentNullException.ThrowIfNull(facts);

        lock (_gate)
        {
            foreach (var id in _collectedFactIds) _facts.TryRemove(id, out _);
            _collectedFactIds.Clear();
            foreach (var fact in facts)
            {
                _facts[fact.Id] = fact;
                _collectedFactIds.Add(fact.Id);
            }
            _complete = complete;
            Interlocked.Increment(ref _version);
            return Build();
        }
    }

    public void Record(KoanFactDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var fact = KoanFact.Create(
            descriptor.Code,
            descriptor.Kind,
            descriptor.State,
            descriptor.Subject,
            descriptor.Summary,
            descriptor.ReasonCode,
            descriptor.Correction,
            descriptor.Source,
            descriptor.CorrelationId,
            descriptor.ObservedAtUtc);

        // The decision is already held as it stands: nothing to write, no lock taken.
        if (_facts.TryGetValue(fact.Id, out var held) && SameDecision(held, fact)) return;

        lock (_gate)
        {
            _facts[fact.Id] = fact;
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Whether two facts with one id say the same thing; when it was observed is not part of the decision.</summary>
    private static bool SameDecision(KoanFact held, KoanFact fact) =>
        held.Kind == fact.Kind &&
        held.State == fact.State &&
        string.Equals(held.Summary, fact.Summary, StringComparison.Ordinal) &&
        string.Equals(held.ReasonCode, fact.ReasonCode, StringComparison.Ordinal) &&
        string.Equals(held.Correction, fact.Correction, StringComparison.Ordinal) &&
        string.Equals(held.Source, fact.Source, StringComparison.Ordinal) &&
        string.Equals(held.CorrelationId, fact.CorrelationId, StringComparison.Ordinal);

    /// <summary>Builds the ordered envelope for the facts held now. Runs under the gate.</summary>
    private KoanFactEnvelope Build()
    {
        var version = Volatile.Read(ref _version);
        var ordered = _facts.Values
            .OrderBy(fact => fact.Kind)
            .ThenBy(fact => fact.Code, StringComparer.Ordinal)
            .ThenBy(fact => fact.Subject, StringComparer.Ordinal)
            .ThenBy(fact => fact.CorrelationId, StringComparer.Ordinal)
            .ToArray();

        var built = new KoanFactEnvelope(
            Constants.Diagnostics.FactSchemaVersion,
            version,
            _sessionId,
            DateTimeOffset.UtcNow,
            _complete,
            ordered);
        Volatile.Write(ref _current, built);
        return built;
    }
}
