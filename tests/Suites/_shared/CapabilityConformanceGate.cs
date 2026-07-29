using Koan.Core.Capabilities;
using Xunit;

namespace Koan.Data.Conformance;

/// <summary>Defines the required behavior when an adapter does not announce a tested capability.</summary>
public enum UnclaimedDisposition
{
    /// <summary>The capability is mandatory; its proof runs and the declaration check also fails when absent.</summary>
    Required,

    /// <summary>The capability is optional, but its absence must reject the scoped operation safely.</summary>
    FailClosed,

    /// <summary>The capability is optional and its proof is reported as skipped when absent.</summary>
    Skip,
}

/// <summary>
/// Runs capability-bound conformance proofs without letting an absent or false claim read as green.
/// This test-only source is link-compiled into each AODB test kit so concrete adapter suites do not
/// acquire a transitive runtime dependency merely to execute the capability dispatch.
/// </summary>
public static class CapabilityConformanceGate
{
    /// <summary>Runs the realization, fail-closed, or loud-skip path selected by the capability declaration.</summary>
    public static Task RunCell(
        CapabilitySet declared,
        IReadOnlyList<(Capability Token, UnclaimedDisposition Disposition)> modules,
        Capability token,
        Func<Task> realize,
        Func<Task>? failClosed = null)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(realize);

        var disposition = FindDisposition(modules, token);
        if (disposition == UnclaimedDisposition.FailClosed && failClosed is null)
        {
            throw new InvalidOperationException(
                $"Capability '{token}' requires a fail-closed proof, but the conformance cell did not supply one.");
        }

        if (declared.Has(token) || disposition == UnclaimedDisposition.Required)
        {
            return realize();
        }

        if (disposition == UnclaimedDisposition.FailClosed)
        {
            return failClosed!();
        }

        Assert.Skip($"Capability '{token}' is not announced.");
        throw new InvalidOperationException("xUnit returned from Assert.Skip instead of reporting a skipped test.");
    }

    /// <summary>Fails when any mandatory capability is not announced.</summary>
    public static void AssertRequiredDeclared(
        CapabilitySet declared,
        IReadOnlyList<(Capability Token, UnclaimedDisposition Disposition)> modules)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var (token, disposition) in modules)
        {
            if (disposition == UnclaimedDisposition.Required)
            {
                Assert.True(declared.Has(token), $"The adapter must announce required capability '{token}'.");
            }
        }
    }

    private static UnclaimedDisposition FindDisposition(
        IReadOnlyList<(Capability Token, UnclaimedDisposition Disposition)> modules,
        Capability token)
    {
        foreach (var (candidate, disposition) in modules)
        {
            if (candidate == token)
            {
                return disposition;
            }
        }

        throw new InvalidOperationException(
            $"Capability '{token}' is not registered in this conformance suite.");
    }
}
