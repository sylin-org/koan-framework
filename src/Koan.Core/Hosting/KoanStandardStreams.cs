using System;
using System.IO;
using System.Linq;

namespace Koan.Core.Hosting;

/// <summary>What this process's standard output carries.</summary>
public enum StandardOutputChannel
{
    /// <summary>Human-readable diagnostics: the boot report and console logging.</summary>
    Diagnostic,

    /// <summary>A machine protocol. Nothing else may write to standard output.</summary>
    Protocol
}

/// <summary>
/// The single, process-global answer to "what does standard output carry?".
/// </summary>
/// <remarks>
/// Standard output is a process singleton and is either a diagnostic channel or a protocol channel,
/// never both. Three components used to decide that independently — the boot report wrote to
/// <see cref="Console"/>, the console logger bound the stdout handle when constructed, and the MCP
/// STDIO transport framed JSON-RPC onto the same stream — so an MCP client received log output
/// interleaved with protocol frames while no component was individually wrong.
///
/// The decision is derived from the process itself, not from composition. That is deliberate and is
/// the whole point: a claim registered during DI is inherently ordered after some writers have bound
/// and before others resolve, so correctness would depend on module ordering — the same race the
/// owner exists to remove. Here the answer is already true before the first byte is written, because
/// the first writer resolves it on its own first use.
///
/// Detection is explicit. Inferring it (for example from <c>Console.IsInputRedirected</c>) would
/// silently relocate logs for ordinary applications under CI, containers, pipes, and IDEs — a worse
/// and far less diagnosable bug than the one being prevented.
/// </remarks>
public static class KoanStandardStreams
{
    // Environment first (an MCP client launches the server with a command line it controls),
    // command-line switch second. Both are known before any Koan code runs.
    private const string ProtocolEnvironmentVariable = "KOAN_MCP_STDIO";
    private const string ProtocolSwitch = "--mcp-stdio";

    private static readonly (StandardOutputChannel Channel, string? Owner, string? Signal) Resolved = Detect();

    /// <summary>What standard output carries in this process. Immutable for the process lifetime.</summary>
    public static StandardOutputChannel StandardOutput => Resolved.Channel;

    /// <summary>True when standard output belongs to a machine protocol.</summary>
    public static bool IsStandardOutputProtocol => Resolved.Channel == StandardOutputChannel.Protocol;

    /// <summary>The capability standard output belongs to, when it carries a protocol.</summary>
    public static string? StandardOutputOwner => Resolved.Owner;

    /// <summary>The launch signal that selected the channel, for startup reporting and facts.</summary>
    public static string? StandardOutputSignal => Resolved.Signal;

    /// <summary>
    /// Where framework diagnostics belong: stderr when standard output carries a protocol, stdout
    /// otherwise. Resolved per call so a single writer can be shared by both shapes.
    /// </summary>
    public static TextWriter Diagnostics => IsStandardOutputProtocol ? Console.Error : Console.Out;

    private static (StandardOutputChannel, string?, string?) Detect()
    {
        var variable = SafeEnvironment(ProtocolEnvironmentVariable);
        if (IsTruthy(variable))
        {
            return (StandardOutputChannel.Protocol, "Koan.Mcp/stdio", $"{ProtocolEnvironmentVariable}={variable}");
        }

        if (SafeArguments().Any(a => string.Equals(a, ProtocolSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            return (StandardOutputChannel.Protocol, "Koan.Mcp/stdio", ProtocolSwitch);
        }

        return (StandardOutputChannel.Diagnostic, null, null);
    }

    private static bool IsTruthy(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && (value.Equals("1", StringComparison.Ordinal)
               || value.Equals("true", StringComparison.OrdinalIgnoreCase)
               || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private static string? SafeEnvironment(string name)
    {
        try { return Environment.GetEnvironmentVariable(name); }
        catch { return null; }
    }

    private static string[] SafeArguments()
    {
        try { return Environment.GetCommandLineArgs(); }
        catch { return Array.Empty<string>(); }
    }
}
