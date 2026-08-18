using System;
using System.IO;
using System.Threading;

namespace Koan.Core.Hosting;

/// <summary>
/// The single owner of this process's standard output.
/// </summary>
/// <remarks>
/// Standard output is a process-global singleton, and it is either a diagnostic channel for humans or
/// a protocol channel for a machine — never both. Before this owner existed the choice was made three
/// times independently: the boot report wrote to <see cref="Console"/>, the console logger bound the
/// stdout handle when it was constructed, and the MCP STDIO transport framed JSON-RPC onto the same
/// stream. Nothing arbitrated, so an MCP client received ~85 lines of log output interleaved with the
/// protocol and no component was individually wrong.
///
/// A capability that needs stdout claims it here, once, during composition. Everything diagnostic then
/// writes to <see cref="Diagnostics"/> and lands on stderr instead. The claim is deliberately explicit:
/// inferring it (for example from <c>Console.IsInputRedirected</c>) would silently relocate logs for
/// ordinary applications running under CI, containers, pipes, and IDEs.
/// </remarks>
public static class KoanStandardStreams
{
    private static int _claimed;
    private static string? _owner;
    private static string? _reason;

    /// <summary>True when a capability has taken standard output for a machine protocol.</summary>
    public static bool IsStandardOutputClaimed => Volatile.Read(ref _claimed) == 1;

    /// <summary>The capability holding standard output, when claimed.</summary>
    public static string? StandardOutputOwner => Volatile.Read(ref _owner);

    /// <summary>Why standard output was claimed, for startup reporting and facts.</summary>
    public static string? StandardOutputReason => Volatile.Read(ref _reason);

    /// <summary>
    /// Where framework diagnostics belong right now: stderr once stdout carries a protocol,
    /// stdout otherwise. Resolved per call so a claim made during composition is honored by
    /// writers that were created earlier.
    /// </summary>
    public static TextWriter Diagnostics => IsStandardOutputClaimed ? Console.Error : Console.Out;

    /// <summary>
    /// Claims standard output for a machine protocol. Idempotent for the same owner; a second,
    /// different claimant is refused rather than allowed to silently corrupt the first one's stream.
    /// </summary>
    public static bool TryClaimStandardOutput(string owner, string reason)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("An owner is required.", nameof(owner));

        if (Interlocked.CompareExchange(ref _claimed, 1, 0) == 0)
        {
            Volatile.Write(ref _owner, owner);
            Volatile.Write(ref _reason, reason);
            return true;
        }

        return string.Equals(Volatile.Read(ref _owner), owner, StringComparison.Ordinal);
    }

    /// <summary>Test seam. Never call from application or framework startup paths.</summary>
    internal static void ResetForTests()
    {
        Volatile.Write(ref _owner, null);
        Volatile.Write(ref _reason, null);
        Volatile.Write(ref _claimed, 0);
    }
}
