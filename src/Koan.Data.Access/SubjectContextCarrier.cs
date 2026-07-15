using System;
using System.Linq;
using Koan.Core.Context;
using Koan.Data.Access.Infrastructure;

namespace Koan.Data.Access;

/// <summary>
/// Carries the ambient <see cref="SubjectContext"/> across a durable hop so work runs under its submitting subject.
/// The opaque wire form is versioned and restoration requires authenticated ingress provenance. Malformed or
/// unsupported data fails with a safe typed error before any subject scope is pushed.
/// </summary>
public sealed class SubjectContextCarrier : IKoanContextCarrier
{
    /// <inheritdoc />
    public string AxisKey => Constants.ContextCarriage.AxisKey;

    /// <inheritdoc />
    public ContextIngressTrust MinimumIngressTrust => ContextIngressTrust.Authenticated;

    /// <inheritdoc />
    public string? Capture()
    {
        var s = Subject.Current;
        if (s is null) return null;                     // unset → nothing to carry
        if (s.IsSystem) return Constants.ContextCarriage.SystemToken;             // explicit elevated scope
        if (!s.IsConstrained) return Constants.ContextCarriage.IdPrefix + s.Id;   // unconstrained subject
        return Constants.ContextCarriage.ScopedPrefix + s.Id + Constants.ContextCarriage.UnitSeparator
            + string.Join(
                Constants.ContextCarriage.UnitSeparator,
                s.Scopes!.OrderBy(static scope => scope, StringComparer.Ordinal)); // deterministic; empty → trailing US
    }

    /// <inheritdoc />
    public IDisposable Restore(string captured)
    {
        if (captured is null)
            throw KoanContextCarrierException.MalformedPayload(AxisKey);

        if (captured == Constants.ContextCarriage.SystemToken) return Subject.System();

        if (captured.StartsWith(Constants.ContextCarriage.IdPrefix, StringComparison.Ordinal))
        {
            var id = captured[Constants.ContextCarriage.IdPrefix.Length..];
            if (string.IsNullOrWhiteSpace(id) || id.IndexOf(Constants.ContextCarriage.UnitSeparator) >= 0)
                throw KoanContextCarrierException.MalformedPayload(AxisKey);
            return Subject.Unconstrained(id);
        }

        if (captured.StartsWith(Constants.ContextCarriage.ScopedPrefix, StringComparison.Ordinal))
        {
            var parts = captured[Constants.ContextCarriage.ScopedPrefix.Length..]
                .Split(Constants.ContextCarriage.UnitSeparator);
            var id = parts[0];
            if (string.IsNullOrWhiteSpace(id))
                throw KoanContextCarrierException.MalformedPayload(AxisKey);
            // parts[1..] are the scope tokens; drop any empties (a trailing US from an empty scope set → constrained-to-nothing).
            return Subject.Use(id, parts.Skip(1).Where(static p => p.Length > 0));
        }

        throw captured.StartsWith(Constants.ContextCarriage.VersionPrefix, StringComparison.Ordinal)
            ? KoanContextCarrierException.MalformedPayload(AxisKey)
            : KoanContextCarrierException.UnsupportedVersion(AxisKey);
    }

    /// <inheritdoc />
    public IDisposable Suppress() => KoanContext.Suppress<SubjectContext>();
}
