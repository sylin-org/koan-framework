using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Koan.Core.Semantics.Segmentation;

namespace Koan.Core.Context;

/// <summary>
/// Joins hard segmentation meaning to opaque context carriage once per CLR subject. Async pillars use this one plan
/// to bind and capture at the terminal, then validate, restore, and re-bind before application work.
/// </summary>
public sealed class SegmentationContextPlan
{
    private readonly SegmentationPlan _segmentation;
    private readonly KoanContextCarrierRegistry _carriers;
    private readonly ConcurrentDictionary<Type, SubjectObligation> _subjects = new();

    public SegmentationContextPlan(
        SegmentationPlan segmentation,
        KoanContextCarrierRegistry carriers)
    {
        _segmentation = segmentation;
        _carriers = carriers;
        MinimumIngressTrust = Strongest(carriers.Descriptors.Select(static carrier => carrier.MinimumIngressTrust));
    }

    /// <summary>The weakest ingress provenance that can restore every composed carrier.</summary>
    public ContextIngressTrust MinimumIngressTrust { get; }

    /// <summary>Binds the subject's hard dimensions and captures one immutable opaque context bag.</summary>
    public IReadOnlyDictionary<string, string>? Capture(Type subject, string operation)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var obligation = For(subject);
        obligation.EnsureCarriers(operation);
        var bindings = obligation.Scope.Bind(operation);
        var captured = _carriers.Capture();
        if (bindings.Length > 0)
        {
            SortedDictionary<string, string>? completed = null;
            foreach (var binding in bindings)
            {
                var axis = obligation.AxisFor(binding.DimensionId);
                if (axis is not null && (captured?.ContainsKey(axis) ?? false)) continue;

                var materialized = _carriers.CaptureRequired(binding.DimensionId, binding.Value);
                if (axis is null || materialized is null) continue;
                if (completed is null)
                {
                    completed = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    if (captured is not null)
                    {
                        foreach (var pair in captured) completed.Add(pair.Key, pair.Value);
                    }
                }
                completed[axis] = materialized;
            }

            if (completed is not null)
                captured = new ReadOnlyDictionary<string, string>(completed);
        }
        obligation.EnsureCaptured(captured, operation);
        return captured;
    }

    /// <summary>
    /// Requires every applicable carried axis, restores it at the supplied provenance, and re-binds segmentation
    /// under that restored scope before returning control to the caller.
    /// </summary>
    public IDisposable Restore(
        Type subject,
        IReadOnlyDictionary<string, string>? captured,
        ContextIngressTrust ingressTrust,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var obligation = For(subject);
        obligation.EnsureCarriers(operation);
        obligation.EnsureCaptured(captured, operation);
        var contextScope = _carriers.Restore(captured, ingressTrust);
        try
        {
            obligation.Scope.Bind(operation);
            return contextScope;
        }
        catch
        {
            contextScope.Dispose();
            throw;
        }
    }

    private SubjectObligation For(Type subject) => _subjects.GetOrAdd(
        subject,
        static (type, state) => state.Compile(type),
        this);

    private SubjectObligation Compile(Type subject)
    {
        var scope = _segmentation.For(subject);
        if (scope.IsEmpty) return new SubjectObligation(scope, []);

        var axes = scope.DimensionIds
            .Select(dimension => _carriers.TryGetSegmentationCarrier(dimension, out var carrier)
                ? new RequiredAxis(dimension, carrier.AxisKey)
                : new RequiredAxis(dimension, null))
            .ToArray();
        return new SubjectObligation(scope, axes);
    }

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

    private sealed class SubjectObligation(
        SegmentationScope scope,
        IReadOnlyList<RequiredAxis> axes)
    {
        public SegmentationScope Scope { get; } = scope;

        public void EnsureCarriers(string operation)
        {
            foreach (var axis in axes)
            {
                if (axis.AxisKey is null)
                    throw SegmentationContextException.MissingCarrier(axis.DimensionId, operation);
            }
        }

        public void EnsureCaptured(IReadOnlyDictionary<string, string>? captured, string operation)
        {
            foreach (var axis in axes)
            {
                if (axis.AxisKey is not null && !(captured?.ContainsKey(axis.AxisKey) ?? false))
                    throw SegmentationContextException.MissingCapturedAxis(axis.DimensionId, operation);
            }
        }

        public string? AxisFor(string dimensionId)
            => axes.FirstOrDefault(axis => string.Equals(
                axis.DimensionId,
                dimensionId,
                StringComparison.OrdinalIgnoreCase))?.AxisKey;
    }

    private sealed record RequiredAxis(string DimensionId, string? AxisKey);
}
