using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AwesomeAssertions;
using Koan.Core.Hosting.Bootstrap;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Specs;

/// <summary>
/// (H9) Pins the <c>KOAN_VERBOSE_ASSEMBLIES</c> gate on <see cref="AppBootstrapper.EmitAssemblySummary"/>:
/// the raw <c>ASSEMBLIES|…</c> lines and the <c>assembly-scan</c> JSON payload are machine-oriented
/// diagnostics that must stay OFF stdout by default (so the boot output stays human) and appear only when
/// verbose is enabled. Calls the internal emitter directly (InternalsVisibleTo) rather than booting a host,
/// so it is deterministic and does not depend on the live assembly closure.
/// </summary>
/// <remarks>
/// Serialized via <see cref="FailLoudBootCollection"/> because it swaps the process-global
/// <see cref="Console.Out"/>. Assertions target the specific ASSEMBLIES tokens (not buffer emptiness) so
/// they are robust to unrelated Console output that other collections may emit during the capture window.
/// </remarks>
[Collection(FailLoudBootCollection.Name)]
public sealed class AssemblySummaryVerboseGateSpec
{
    private static readonly List<(Assembly Assembly, string LoadContext)> SampleLog =
        new() { (typeof(string).Assembly, "<default>") };

    [Fact]
    public void Emits_no_ASSEMBLIES_diagnostics_when_verbose_disabled()
    {
        var captured = CaptureStdout(() =>
            AppBootstrapper.EmitAssemblySummary(SampleLog, new List<Assembly>(), verboseAssemblies: false, lenientAssemblySkips: 0));

        captured.Should().NotContain("ASSEMBLIES|loaded=");
        captured.Should().NotContain("assembly-scan");
        captured.Should().NotContain("ASSEMBLY|");
    }

    [Fact]
    public void Emits_summary_and_json_when_verbose_enabled()
    {
        var captured = CaptureStdout(() =>
            AppBootstrapper.EmitAssemblySummary(SampleLog, new List<Assembly>(), verboseAssemblies: true, lenientAssemblySkips: 0));

        captured.Should().Contain("ASSEMBLIES|loaded=");
        captured.Should().Contain("assembly-scan"); // the JSON payload's "event":"assembly-scan"
        captured.Should().Contain("ASSEMBLIES|lenientSkips=0");
        captured.Should().Contain("ASSEMBLY|"); // the per-assembly detail listing
    }

    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }
}
