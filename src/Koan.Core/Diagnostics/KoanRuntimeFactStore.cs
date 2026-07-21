using Koan.Core.Infrastructure;

namespace Koan.Core.Diagnostics;

internal sealed class KoanRuntimeFactStore : IKoanRuntimeFacts, IKoanRuntimeFactRecorder
{
    private readonly object _gate = new();
    private readonly Dictionary<string, KoanFact> _facts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collectedFactIds = new(StringComparer.Ordinal);
    private readonly string _sessionId = Guid.CreateVersion7().ToString("n");
    private long _sequence;
    private readonly KoanFactEnvelope _initial;
    private KoanFactEnvelope? _current;

    public KoanRuntimeFactStore()
    {
        _initial = new KoanFactEnvelope(
            Constants.Diagnostics.FactSchemaVersion,
            0,
            _sessionId,
            DateTimeOffset.UtcNow,
            false,
            []);
    }

    public KoanFactEnvelope Current
    {
        get
        {
            lock (_gate)
            {
                return _current ?? _initial;
            }
        }
    }

    internal KoanFactEnvelope Replace(IEnumerable<KoanFact> facts, bool complete)
    {
        ArgumentNullException.ThrowIfNull(facts);

        lock (_gate)
        {
            foreach (var id in _collectedFactIds) _facts.Remove(id);
            _collectedFactIds.Clear();
            foreach (var fact in facts)
            {
                _facts[fact.Id] = fact;
                _collectedFactIds.Add(fact.Id);
            }
            return Commit(complete);
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

        lock (_gate)
        {
            _facts[fact.Id] = fact;
            Commit(_current?.Complete ?? false);
        }
    }

    private KoanFactEnvelope Commit(bool complete)
    {
        var ordered = _facts.Values
            .OrderBy(fact => fact.Kind)
            .ThenBy(fact => fact.Code, StringComparer.Ordinal)
            .ThenBy(fact => fact.Subject, StringComparer.Ordinal)
            .ThenBy(fact => fact.CorrelationId, StringComparer.Ordinal)
            .ToArray();

        _current = new KoanFactEnvelope(
            Constants.Diagnostics.FactSchemaVersion,
            ++_sequence,
            _sessionId,
            DateTimeOffset.UtcNow,
            complete,
            ordered);
        return _current;
    }
}
