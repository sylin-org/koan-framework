using System.Collections.ObjectModel;

namespace Koan.Core.Context;

/// <summary>
/// The host-owned registry for durable Koan context. It captures registered axes and restores a bag only after every
/// identity and ingress-trust requirement passes, then unwinds scopes in reverse order.
/// </summary>
public sealed class KoanContextCarrierRegistry
{
    private readonly IReadOnlyList<RegisteredCarrier> _carriers;
    private readonly IReadOnlyDictionary<string, RegisteredCarrier> _byKey;
    private readonly IReadOnlyDictionary<string, RegisteredCarrier> _bySegmentationDimension;

    /// <summary>
    /// A value-free description of one composed carrier. This is safe to project into startup diagnostics: it
    /// exposes the stable axis identity and required provenance, never the carrier instance or captured context.
    /// </summary>
    public sealed record CarrierDescriptor(string AxisKey, ContextIngressTrust MinimumIngressTrust);

    public KoanContextCarrierRegistry(IEnumerable<IKoanContextCarrier> carriers)
    {
        ArgumentNullException.ThrowIfNull(carriers);

        var byKey = new Dictionary<string, RegisteredCarrier>(StringComparer.Ordinal);
        var bySegmentationDimension = new Dictionary<string, RegisteredCarrier>(StringComparer.OrdinalIgnoreCase);
        foreach (var carrier in carriers)
        {
            if (carrier is null)
                throw KoanContextCarrierException.InvalidAxis();

            // Snapshot declarations once. Composition is immutable even if a faulty carrier later returns different
            // property values; a runtime object cannot change its identity or weaken its trust requirement.
            var axisKey = carrier.AxisKey;
            var minimumIngressTrust = carrier.MinimumIngressTrust;
            var segmentationDimensions = SnapshotSegmentationDimensions(carrier.SegmentationDimensions);
            if (!KoanContextCarrierException.IsValidAxisKey(axisKey))
                throw KoanContextCarrierException.InvalidAxis();
            if (!IsDefined(minimumIngressTrust))
                throw new ArgumentOutOfRangeException(nameof(carriers), "A context carrier declares an unknown ingress-trust value.");

            var registration = new RegisteredCarrier(axisKey, minimumIngressTrust, segmentationDimensions, carrier);
            if (!byKey.TryAdd(axisKey, registration))
                throw KoanContextCarrierException.DuplicateAxis(axisKey);
            foreach (var dimension in segmentationDimensions)
            {
                if (!bySegmentationDimension.TryAdd(dimension, registration))
                {
                    throw new InvalidOperationException(
                        $"Segmentation dimension '{dimension}' is represented by more than one context carrier. " +
                        "One carrier must own each hard async-context mapping.");
                }
            }
        }

        _carriers = Array.AsReadOnly(
            byKey.Values.OrderBy(static carrier => carrier.AxisKey, StringComparer.Ordinal).ToArray());
        _byKey = new ReadOnlyDictionary<string, RegisteredCarrier>(byKey);
        _bySegmentationDimension = new ReadOnlyDictionary<string, RegisteredCarrier>(bySegmentationDimension);
        Descriptors = Array.AsReadOnly(
            _carriers.Select(static carrier =>
                    new CarrierDescriptor(carrier.AxisKey, carrier.MinimumIngressTrust))
                .ToArray());
    }

    /// <summary>
    /// Gets the ordinally ordered, value-free carrier composition for inspection and startup reporting.
    /// </summary>
    public IReadOnlyList<CarrierDescriptor> Descriptors { get; }

    internal bool TryGetSegmentationCarrier(string dimensionId, out CarrierDescriptor descriptor)
    {
        if (_bySegmentationDimension.TryGetValue(dimensionId, out var carrier))
        {
            descriptor = new CarrierDescriptor(carrier.AxisKey, carrier.MinimumIngressTrust);
            return true;
        }

        descriptor = null!;
        return false;
    }

    internal string? CaptureRequired(string dimensionId, string value)
    {
        if (!_bySegmentationDimension.TryGetValue(dimensionId, out var carrier)) return null;
        try
        {
            return carrier.Instance.CaptureRequired(dimensionId, value);
        }
        catch
        {
            throw KoanContextCarrierException.CaptureFailed(carrier.AxisKey);
        }
    }

    /// <summary>
    /// Captures every present registered axis into an immutable-to-callers, ordinally ordered bag. Returns
    /// <c>null</c> without allocation when no carrier contributes a value.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Capture()
    {
        SortedDictionary<string, string>? captured = null;
        for (var i = 0; i < _carriers.Count; i++)
        {
            var carrier = _carriers[i];
            string? value;
            try
            {
                value = carrier.Instance.Capture();
            }
            catch (KoanContextCarrierException)
            {
                // Carrier exceptions are mutable through Exception.Data/HelpLink/Source. Recreate them at the host
                // boundary so a module cannot smuggle context values into logs or durable failure records.
                throw KoanContextCarrierException.CaptureFailed(carrier.AxisKey);
            }
            catch
            {
                throw KoanContextCarrierException.CaptureFailed(carrier.AxisKey);
            }

            if (value is null) continue;
            (captured ??= new SortedDictionary<string, string>(StringComparer.Ordinal))[carrier.AxisKey] = value;
        }

        return captured is null
            ? null
            : new ReadOnlyDictionary<string, string>(captured);
    }

    /// <summary>
    /// Restores or suppresses every registered axis for the returned scope. Unknown axes and insufficient trust fail
    /// before any carrier is invoked; unexpected carrier failures are sanitized and partial scopes are unwound.
    /// </summary>
    public IDisposable Restore(
        IReadOnlyDictionary<string, string>? captured,
        ContextIngressTrust ingressTrust)
    {
        if (!IsDefined(ingressTrust))
            throw new ArgumentOutOfRangeException(nameof(ingressTrust));

        if (_carriers.Count == 0 && (captured is null || captured.Count == 0)) return NoopScope.Instance;

        // Keep the empty hot path outside the LINQ-bearing slow path so the compiler does not allocate a closure
        // before returning the shared no-op scope.
        return RestoreCore(captured, ingressTrust);
    }

    private IDisposable RestoreCore(
        IReadOnlyDictionary<string, string>? captured,
        ContextIngressTrust ingressTrust)
    {

        Dictionary<string, string>? snapshot = null;
        if (captured is { Count: > 0 })
        {
            snapshot = new Dictionary<string, string>(captured.Count, StringComparer.Ordinal);
            foreach (var pair in captured)
            {
                if (!KoanContextCarrierException.IsValidAxisKey(pair.Key))
                    throw KoanContextCarrierException.InvalidAxis();
                if (pair.Value is null)
                    throw KoanContextCarrierException.MalformedPayload(pair.Key);
                snapshot.Add(pair.Key, pair.Value);
            }

            var unknown = snapshot.Keys.Where(key => !_byKey.ContainsKey(key)).ToArray();
            if (unknown.Length > 0) throw KoanContextCarrierException.UnknownAxes(unknown);

            var underTrusted = snapshot.Keys
                .Select(key => _byKey[key])
                .Where(carrier => !Meets(ingressTrust, carrier.MinimumIngressTrust))
                .ToArray();
            if (underTrusted.Length > 0)
            {
                var required = Strongest(underTrusted.Select(static carrier => carrier.MinimumIngressTrust));
                throw KoanContextCarrierException.InsufficientTrust(
                    underTrusted.Select(static carrier => carrier.AxisKey),
                    required,
                    ingressTrust);
            }
        }

        if (_carriers.Count == 0) return NoopScope.Instance;

        var scopes = new List<CarrierScope>(_carriers.Count);
        for (var i = 0; i < _carriers.Count; i++)
        {
            var carrier = _carriers[i];
            string? value = null;
            var hasValue = snapshot is not null && snapshot.TryGetValue(carrier.AxisKey, out value);
            try
            {
                var scope = hasValue ? carrier.Instance.Restore(value!) : carrier.Instance.Suppress();
                scopes.Add(new CarrierScope(
                    carrier.AxisKey,
                    scope ?? throw new InvalidOperationException("A context carrier returned a null scope.")));
            }
            catch (KoanContextCarrierException failure)
            {
                BestEffortUnwind(scopes);
                throw SanitizeCarrierFailure(failure, carrier.AxisKey, hasValue);
            }
            catch
            {
                BestEffortUnwind(scopes);
                throw hasValue
                    ? KoanContextCarrierException.RestoreFailed(carrier.AxisKey)
                    : KoanContextCarrierException.SuppressionFailed(carrier.AxisKey);
            }
        }

        return new CompositeScope(scopes);
    }

    private static bool Meets(ContextIngressTrust provided, ContextIngressTrust required)
        => required switch
        {
            ContextIngressTrust.Unverified => true,
            ContextIngressTrust.Authenticated => provided is ContextIngressTrust.Authenticated or ContextIngressTrust.HostTrusted,
            ContextIngressTrust.HostTrusted => provided is ContextIngressTrust.HostTrusted,
            _ => false
        };

    private static bool IsDefined(ContextIngressTrust trust)
        => trust is ContextIngressTrust.Unverified or ContextIngressTrust.Authenticated or ContextIngressTrust.HostTrusted;

    private static ContextIngressTrust Strongest(IEnumerable<ContextIngressTrust> requirements)
    {
        var strongest = ContextIngressTrust.Unverified;
        foreach (var requirement in requirements)
        {
            if (requirement == ContextIngressTrust.HostTrusted) return ContextIngressTrust.HostTrusted;
            if (requirement == ContextIngressTrust.Authenticated) strongest = ContextIngressTrust.Authenticated;
        }

        return strongest;
    }

    private static string[] SnapshotSegmentationDimensions(IReadOnlyCollection<string>? dimensions)
    {
        if (dimensions is null)
            throw new InvalidOperationException("A context carrier returned a null segmentation-dimension declaration.");

        try
        {
            return dimensions
                .Select(static dimension => new Semantics.SemanticId(dimension).Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception error) when (error is ArgumentException or NullReferenceException)
        {
            throw new InvalidOperationException(
                "A context carrier declared an invalid segmentation-dimension identity.");
        }
    }

    private static KoanContextCarrierException SanitizeCarrierFailure(
        KoanContextCarrierException failure,
        string axisKey,
        bool restoring)
    {
        if (!restoring) return KoanContextCarrierException.SuppressionFailed(axisKey);

        return failure.Failure switch
        {
            KoanContextCarrierException.FailureKind.MalformedPayload =>
                KoanContextCarrierException.MalformedPayload(axisKey),
            KoanContextCarrierException.FailureKind.UnsupportedVersion =>
                KoanContextCarrierException.UnsupportedVersion(axisKey),
            _ => KoanContextCarrierException.RestoreFailed(axisKey)
        };
    }

    private static void BestEffortUnwind(IReadOnlyList<CarrierScope> scopes)
    {
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            try { scopes[i].Scope.Dispose(); }
            catch { /* best effort: never hide the original carriage failure */ }
        }
    }

    private static void DisposeScopes(IReadOnlyList<CarrierScope> scopes)
    {
        List<string>? failedAxes = null;
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            try { scopes[i].Scope.Dispose(); }
            catch { (failedAxes ??= []).Add(scopes[i].AxisKey); }
        }

        if (failedAxes is not null)
            throw KoanContextCarrierException.ScopeDisposalFailed(failedAxes);
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    private sealed record RegisteredCarrier(
        string AxisKey,
        ContextIngressTrust MinimumIngressTrust,
        IReadOnlyList<string> SegmentationDimensions,
        IKoanContextCarrier Instance);

    private sealed record CarrierScope(string AxisKey, IDisposable Scope);

    private sealed class CompositeScope(IReadOnlyList<CarrierScope> scopes) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            DisposeScopes(scopes);
        }
    }
}
