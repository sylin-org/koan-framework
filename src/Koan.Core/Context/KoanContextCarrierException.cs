using System.Collections.ObjectModel;

namespace Koan.Core.Context;

/// <summary>
/// A safe, machine-readable context-carriage failure. Messages expose bounded axis identities and trust posture only;
/// carried values and implementation exceptions are deliberately excluded.
/// </summary>
public sealed class KoanContextCarrierException : InvalidOperationException
{
    private const int MaximumAxisKeyLength = 128;
    private const int MaximumReportedAxisKeys = 16;
    private const string InvalidAxisIdentity = "<invalid-axis>";

    /// <summary>The stable failure taxonomy used by durable consumers and diagnostics.</summary>
    public enum FailureKind
    {
        InvalidAxis,
        DuplicateAxis,
        UnknownAxis,
        InsufficientIngressTrust,
        MalformedPayload,
        UnsupportedVersion,
        CaptureFailed,
        RestoreFailed,
        SuppressionFailed,
        ScopeDisposalFailed
    }

    private KoanContextCarrierException(
        FailureKind failure,
        IEnumerable<string>? axisKeys = null,
        ContextIngressTrust? requiredTrust = null,
        ContextIngressTrust? providedTrust = null)
        : base(BuildMessage(failure, axisKeys, requiredTrust, providedTrust, out var safeKeys))
    {
        Failure = failure;
        AxisKeys = new ReadOnlyCollection<string>(safeKeys);
        RequiredTrust = requiredTrust;
        ProvidedTrust = providedTrust;
    }

    /// <summary>The machine-readable failure category.</summary>
    public FailureKind Failure { get; }

    /// <summary>A safe, bounded subset of carrier identities implicated in the failure.</summary>
    public IReadOnlyList<string> AxisKeys { get; }

    /// <summary>The strongest minimum trust required by the rejected axes, when applicable.</summary>
    public ContextIngressTrust? RequiredTrust { get; }

    /// <summary>The trust supplied by the ingress, when applicable.</summary>
    public ContextIngressTrust? ProvidedTrust { get; }

    /// <summary>Creates a safe malformed-payload refusal for a carrier.</summary>
    public static KoanContextCarrierException MalformedPayload(string axisKey)
        => new(FailureKind.MalformedPayload, [axisKey]);

    /// <summary>Creates a safe unsupported-version refusal for a carrier.</summary>
    public static KoanContextCarrierException UnsupportedVersion(string axisKey)
        => new(FailureKind.UnsupportedVersion, [axisKey]);

    internal static KoanContextCarrierException InvalidAxis()
        => new(FailureKind.InvalidAxis);

    internal static KoanContextCarrierException DuplicateAxis(string axisKey)
        => new(FailureKind.DuplicateAxis, [axisKey]);

    internal static KoanContextCarrierException UnknownAxes(IEnumerable<string> axisKeys)
        => new(FailureKind.UnknownAxis, axisKeys);

    internal static KoanContextCarrierException InsufficientTrust(
        IEnumerable<string> axisKeys,
        ContextIngressTrust required,
        ContextIngressTrust provided)
        => new(FailureKind.InsufficientIngressTrust, axisKeys, required, provided);

    internal static KoanContextCarrierException CaptureFailed(string axisKey)
        => new(FailureKind.CaptureFailed, [axisKey]);

    internal static KoanContextCarrierException RestoreFailed(string axisKey)
        => new(FailureKind.RestoreFailed, [axisKey]);

    internal static KoanContextCarrierException SuppressionFailed(string axisKey)
        => new(FailureKind.SuppressionFailed, [axisKey]);

    internal static KoanContextCarrierException ScopeDisposalFailed(IEnumerable<string> axisKeys)
        => new(FailureKind.ScopeDisposalFailed, axisKeys);

    internal static bool IsValidAxisKey(string? axisKey)
    {
        if (string.IsNullOrEmpty(axisKey) || axisKey.Length > MaximumAxisKeyLength) return false;
        for (var i = 0; i < axisKey.Length; i++)
        {
            var value = axisKey[i];
            if ((value >= 'a' && value <= 'z') ||
                (value >= '0' && value <= '9') ||
                value is '.' or ':' or '_' or '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static string BuildMessage(
        FailureKind failure,
        IEnumerable<string>? axisKeys,
        ContextIngressTrust? requiredTrust,
        ContextIngressTrust? providedTrust,
        out List<string> safeKeys)
    {
        safeKeys = axisKeys?
            .Select(static key => IsValidAxisKey(key) ? key : InvalidAxisIdentity)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .Take(MaximumReportedAxisKeys)
            .ToList() ?? [];

        var identities = safeKeys.Count == 0 ? string.Empty : $" Axes: [{string.Join(", ", safeKeys)}].";
        return failure switch
        {
            FailureKind.InvalidAxis => "Koan context composition contains an invalid carrier axis identity.",
            FailureKind.DuplicateAxis => $"Koan context composition contains a duplicate carrier axis.{identities}",
            FailureKind.UnknownAxis => $"Carried Koan context contains an axis this host did not compose.{identities}",
            FailureKind.InsufficientIngressTrust =>
                $"Koan context ingress trust '{providedTrust}' does not satisfy required trust '{requiredTrust}'.{identities}",
            FailureKind.MalformedPayload => $"A Koan context carrier rejected malformed payload data.{identities}",
            FailureKind.UnsupportedVersion => $"A Koan context carrier rejected an unsupported payload version.{identities}",
            FailureKind.CaptureFailed => $"A Koan context carrier failed while capturing its axis.{identities}",
            FailureKind.RestoreFailed => $"A Koan context carrier failed while restoring its axis.{identities}",
            FailureKind.SuppressionFailed => $"A Koan context carrier failed while suppressing its axis.{identities}",
            FailureKind.ScopeDisposalFailed => $"One or more Koan context carrier scopes failed while disposing.{identities}",
            _ => "Koan context carriage failed."
        };
    }
}
